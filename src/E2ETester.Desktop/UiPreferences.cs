using System.IO;
using System.Text.Json;
namespace E2ETester.Desktop;

/// <summary>
/// Per-user view state of the main window: which optional sections are open and the last self-declared reviewer.
/// Display convenience only; never part of a package, draft or report. Stored beside the shortcut settings and
/// written atomically like <see cref="UpdatePreferences"/>. Smoke runs point it at their temporary workspace.
/// </summary>
internal sealed record UiPreferences(bool EditPackageExpanded = false, bool InstructionsCollapsed = false, bool AttemptHistoryExpanded = false, string LastReviewer = "")
{
    internal static string DefaultPath => Path.Combine(RememberedSessions.Root, "Settings", "ui.json");

    internal static UiPreferences Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(path)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(this)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
