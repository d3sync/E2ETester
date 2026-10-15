using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace E2ETester.Desktop;

internal sealed record ShortcutDefinition(string Id, string Label, string Scope, Key Key, ModifierKeys Modifiers);
internal sealed record ShortcutKey(Key Key, ModifierKeys Modifiers)
{
    public string Display => Key == Key.None ? "Disabled" : new KeyGesture(Key, Modifiers).GetDisplayStringForCulture(System.Globalization.CultureInfo.CurrentUICulture);
}
internal static class ShortcutPreferences
{
    internal static string DefaultPath => Path.Combine(RememberedSessions.Root, "Settings", "shortcuts.json");
    internal static readonly ShortcutDefinition[] Definitions = [
        new("send", "Send / complete sign-in step", "Workspace", Key.S, ModifierKeys.Alt),
        new("next", "Next case", "Workspace", Key.D, ModifierKeys.Alt),
        new("previous", "Previous case", "Workspace", Key.A, ModifierKeys.Alt),
        new("prepare", "Prepare case", "Workspace", Key.W, ModifierKeys.Alt),
        new("actions", "Guided Actions", "Workspace", Key.G, ModifierKeys.Alt),
        new("database", "Query Studio", "Workspace", Key.Q, ModifierKeys.Alt),
        new("report", "View report", "Workspace", Key.R, ModifierKeys.Alt),
        new("cancel", "Cancel current request", "Workspace", Key.X, ModifierKeys.Alt),
        new("signin", "Sign in with Swagger", "Workspace", Key.B, ModifierKeys.Alt),
        new("save", "Save local draft", "Workspace", Key.S, ModifierKeys.Control),
        new("open", "Open package", "Workspace", Key.O, ModifierKeys.Control),
        new("help", "Keyboard shortcuts", "Workspace", Key.F1, ModifierKeys.None),
        new("swagger-use", "Read and use token", "Swagger sign-in", Key.S, ModifierKeys.Alt)
    ];
    internal static Dictionary<string, ShortcutKey> Defaults() => Definitions.ToDictionary(d => d.Id, d => new ShortcutKey(d.Key, d.Modifiers));
    internal static Dictionary<string, ShortcutKey> Load(string path)
    {
        var result = Defaults();
        try
        {
            if (!File.Exists(path)) return result;
            var saved = JsonSerializer.Deserialize<Dictionary<string, ShortcutKey>>(File.ReadAllText(path));
            foreach (var definition in Definitions)
                if (saved?.TryGetValue(definition.Id, out var key) == true && key is not null) result[definition.Id] = key;
            return Validate(result) is null ? result : Defaults();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return Defaults(); }
    }
    internal static string? Validate(IReadOnlyDictionary<string, ShortcutKey> values)
    {
        foreach (var definition in Definitions)
        {
            if (!values.TryGetValue(definition.Id, out var key)) return "A shortcut is missing.";
            if (key.Key == Key.None) continue;
            if (!Enum.IsDefined(key.Key) || key.Key is Key.System or Key.ImeProcessed or Key.DeadCharProcessed ||
                key.Key is >= Key.LeftShift and <= Key.RightAlt || key.Key is Key.LWin or Key.RWin ||
                (key.Modifiers & ~(ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) != 0 ||
                (key.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == (ModifierKeys.Control | ModifierKeys.Alt))
                return "Use Ctrl or Alt (not both), optionally with Shift. Windows and AltGr combinations are reserved.";
            if ((key.Modifiers == ModifierKeys.None || key.Modifiers == ModifierKeys.Shift) && !(key.Key >= Key.F1 && key.Key <= Key.F12))
                return "Use Ctrl or Alt with a key, or a function key, to preserve normal typing.";
            if (key.Key is Key.Tab or Key.Escape || key.Key == Key.F4 && key.Modifiers.HasFlag(ModifierKeys.Alt) ||
                key.Modifiers == ModifierKeys.Control && key.Key is Key.A or Key.C or Key.V or Key.X or Key.Z or Key.Y ||
                key.Key is Key.F10 or Key.F12)
                return "That combination is reserved for editing, navigation or system controls.";
            try { _ = new KeyGesture(key.Key, key.Modifiers); }
            catch (NotSupportedException) { return "This key combination is not supported."; }
        }
        var duplicate = Definitions.Where(d => values[d.Id].Key != Key.None).GroupBy(d => (d.Scope, values[d.Id])).FirstOrDefault(g => g.Count() > 1);
        return duplicate is null ? null : "Choose different shortcuts for " + string.Join(" and ", duplicate.Select(d => d.Label)) + ".";
    }
    internal static void Save(string path, Dictionary<string, ShortcutKey> values)
    {
        if (Validate(values) is { } error) throw new InvalidDataException(error);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
