using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileScanner.Core;

public enum AppTheme { System, Light, Dark }

// Choices from the settings menu, stored in %LOCALAPPDATA%\FileScanner\settings.json.
public sealed class Settings
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    static readonly string[] Languages = ["en", "es"];

    public static string DefaultFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileScanner", "settings.json");

    public AppTheme Theme { get; set; }
    public string Language { get; set; } = "en";

    // Texts and number formats: "350.2 GB" in English, "350,2 GB" in Spanish.
    [JsonIgnore]
    public CultureInfo Culture => CultureInfo.GetCultureInfo(Language == "es" ? "es-ES" : "en-US");

    // A missing, unreadable or hand-broken file gives the defaults: the app starts anyway.
    public static Settings Load(string file)
    {
        Settings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(file), Json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Settings();
        }
        if (settings is null) return new Settings();
        // JsonStringEnumConverter also accepts numbers, so "Theme": 7 gets here.
        if (!Enum.IsDefined(settings.Theme)) settings.Theme = AppTheme.System;
        if (!Languages.Contains(settings.Language)) settings.Language = "en";
        return settings;
    }

    // Temp file per process: during a restart the closing and the new instance can both write.
    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temp = $"{file}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, file, overwrite: true);
    }
}
