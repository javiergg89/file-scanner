using System.ComponentModel;
using System.Globalization;
using FileScanner.Core;
using Microsoft.UI.Xaml;

namespace FileScanner;

public enum JobState { Waiting, Running, Done, Failed, Cancelled }

// One queued deletion. The Windows progress dialog shows the details (percent, speed);
// this class only tracks each job's state.
public sealed partial class DeleteJob(string title, IReadOnlyList<string> paths, long bytes, bool toRecycleBin) : INotifyPropertyChanged
{
    public string Title { get; } = title;
    // Only the count: the job stays in the queue until "Quitar terminados" and must not hold thousands of paths.
    public int Count { get; } = paths.Count;
    public bool ToRecycleBin { get; } = toRecycleBin;
    public JobState State { get; private set; }
    public int Left { get; private set; }
    public bool IsFinished => State is JobState.Done or JobState.Failed or JobState.Cancelled;
    public Visibility RunningVisibility => State == JobState.Running ? Visibility.Visible : Visibility.Collapsed;

    public string Detail
    {
        get
        {
            string what = $"{Count:N0} {(Count == 1 ? Strings.Cleanup.ItemOne : Strings.Cleanup.ItemMany)} · {DirNode.Format(bytes)}";
            var c = CultureInfo.CurrentCulture;
            return State switch
            {
                JobState.Waiting => string.Format(c, ToRecycleBin ? Strings.Cleanup.JobWaitingRecycle : Strings.Cleanup.JobWaitingPermanent, what),
                JobState.Running => string.Format(c, ToRecycleBin ? Strings.Cleanup.JobRunningRecycle : Strings.Cleanup.JobRunningPermanent, what),
                JobState.Done => string.Format(c, ToRecycleBin ? Strings.Cleanup.JobDoneRecycle : Strings.Cleanup.JobDonePermanent, what),
                JobState.Cancelled => Count == 1 ? Strings.Cleanup.JobCancelledOne : string.Format(c, Strings.Cleanup.JobCancelledMany, Left, Count),
                _ => Count == 1 ? Strings.Cleanup.JobFailedOne : string.Format(c, Strings.Cleanup.JobFailedMany, Left, Count),
            };
        }
    }

    public void Set(JobState state, int left = 0)
    {
        State = state;
        Left = left;
        foreach (var name in new[] { nameof(State), nameof(Detail), nameof(RunningVisibility), nameof(IsFinished) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // A cancel in the Windows dialog counts as Cancelled, not as a permission failure.
    public void Finish(DeleteOutcome outcome) =>
        Set(outcome.Left.Count == 0 ? JobState.Done : outcome.Aborted ? JobState.Cancelled : JobState.Failed, outcome.Left.Count);

    public event PropertyChangedEventHandler? PropertyChanged;
}
