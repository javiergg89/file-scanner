using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileScanner.Core;

public static partial class Shell
{
    [StructLayout(LayoutKind.Sequential)]
    struct FileOp
    {
        public IntPtr Hwnd;
        public uint Func;
        public IntPtr From;
        public IntPtr To;
        public ushort Flags;
        public int AnyAborted;
        public IntPtr NameMappings;
        public IntPtr ProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RecycleInfo
    {
        public int Size;
        public long Bytes;
        public long Items;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    private static partial int SHFileOperation(ref FileOp op);

    [LibraryImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHQueryRecycleBin(string? root, ref RecycleInfo info);

    [LibraryImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);

    const uint FoDelete = 3;
    const ushort FofNoConfirmation = 0x10, FofAllowUndo = 0x40, FofNoErrorUi = 0x400, FofWantNukeWarning = 0x4000;

    // Absolute path: a bare "explorer.exe" could run a fake one from the current directory or the PATH.
    static readonly string Explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    public static void Open(string path) => Process.Start(new ProcessStartInfo(Explorer) { ArgumentList = { path } });

    // explorer.exe expects "/select,<path>" as a single argument with the path in quotes.
    public static void Reveal(string path) => Process.Start(Explorer, $"/select,\"{path}\"");

    const int ErrorNoAssociation = 1155;
    static readonly string RunDll = Path.Combine(Environment.SystemDirectory, "rundll32.exe");

    // Opens the file with its associated program. If the extension has no association (or its program was
    // uninstalled, like an .ifc with no viewer), shows the Windows "Open with" dialog. OpenAs_RunDLL is used
    // because the "openas" verb also fails when the file type is registered with no actions.
    // OpenAs_RunDLL takes the rest of the command line as the path, without quotes.
    // Other errors (file gone, access denied) surface as Win32Exception so the user is told.
    public static void Launch(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorNoAssociation)
        {
            Process.Start(RunDll, $"shell32.dll,OpenAs_RunDLL {path}");
        }
    }

    // Deletes on an STA thread (the shell API requires one). Returns the paths that still exist (in use, access
    // denied, or skipped at the permanent-delete prompt) and whether the user cancelled in the Windows dialog,
    // so leftovers from a cancel are not reported as permission errors.
    public static Task<DeleteOutcome> DeleteWithOutcomeAsync(IReadOnlyList<string> paths, bool toRecycleBin)
    {
        var done = new TaskCompletionSource<DeleteOutcome>();
        var thread = new Thread(() =>
        {
            try
            {
                bool aborted = Delete(paths, toRecycleBin);
                done.SetResult(new([.. paths.Where(p => File.Exists(p) || Directory.Exists(p))], aborted));
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    const int ErrorCancelled = 1223;

    // Returns true if the user cancelled (in the progress dialog or at the permanent-delete prompt).
    // Other errors are not told apart here; the caller checks on disk what is left.
    static bool Delete(IReadOnlyList<string> paths, bool toRecycleBin)
    {
        if (paths.Count == 0) return false;
        // No FOF_SILENT: Windows shows its progress dialog (percentage, speed, cancel). No owner window
        // (Hwnd 0), so the app stays usable and more deletes can be queued.
        // FOF_NOERRORUI: no popup per locked file; leftovers are reported at the end.
        // FOF_WANTNUKEWARNING: if something does not fit in the Recycle Bin, Windows asks before deleting it permanently.
        ushort flags = FofNoConfirmation | FofNoErrorUi;
        if (toRecycleBin) flags |= FofAllowUndo | FofWantNukeWarning;
        var from = Marshal.StringToHGlobalUni(string.Join('\0', paths) + "\0\0");
        try
        {
            var op = new FileOp { Func = FoDelete, From = from, Flags = flags };
            int result = SHFileOperation(ref op);
            return op.AnyAborted != 0 || result == ErrorCancelled;
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    public static long RecycleBinBytes()
    {
        var info = new RecycleInfo { Size = Marshal.SizeOf<RecycleInfo>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? info.Bytes : 0;
    }

    // Returns the HRESULT. Flags 0x1 | 0x4 (SHERB_NOCONFIRMATION | SHERB_NOSOUND): the app asks for confirmation in its own dialog.
    public static int EmptyRecycleBin() => SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x4);
}

// Result of a delete: the paths still on disk and whether the user cancelled.
public sealed record DeleteOutcome(List<string> Left, bool Aborted);
