using System.IO;
using System.Windows;
using System.Windows.Media;

namespace E2ETester.Desktop;
public static class ThemeManager
{
    public static bool IsDark { get; private set; }
    private static string PreferencePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "E2ETester", "theme.txt");
    public static void Initialize(bool loadPreference)
    {
        var dark = false;
        try { dark = loadPreference && File.Exists(PreferencePath) && File.ReadAllText(PreferencePath) == "dark"; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Apply(dark, false);
    }
    public static void Apply(bool dark, bool persist = true)
    {
        IsDark = dark;
        var resources = System.Windows.Application.Current.Resources;
        var colors = new Dictionary<string, string>
        {
            ["Ink"] = dark ? "#e6edf3" : "#1f2328", ["Muted"] = dark ? "#9198a1" : "#59636e",
            ["Line"] = dark ? "#3d444d" : "#d1d9e0", ["Canvas"] = dark ? "#0d1117" : "#f6f8fa",
            ["Surface"] = dark ? "#161b22" : "#ffffff", ["ControlBackground"] = dark ? "#0d1117" : "#ffffff",
            ["Selection"] = dark ? "#1f334a" : "#ddf4ff", ["Accent"] = dark ? "#238636" : "#1f883d",
            ["AccentInk"] = dark ? "#3fb950" : "#116329", ["PrimaryText"] = "#ffffff",
            ["DangerInk"] = dark ? "#ff7b72" : "#cf222e",
            ["Link"] = dark ? "#58a6ff" : "#0969da", ["HeaderBackground"] = dark ? "#010409" : "#f6f8fa"
        };
        foreach (var (key, value) in colors) resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        foreach (var (key, name) in new[] { (SystemColors.WindowBrushKey, "Surface"), (SystemColors.WindowTextBrushKey, "Ink"), (SystemColors.ControlBrushKey, "Canvas"), (SystemColors.ControlTextBrushKey, "Ink"), (SystemColors.HighlightBrushKey, "Selection"), (SystemColors.HighlightTextBrushKey, "Ink"), (SystemColors.GrayTextBrushKey, "Muted") }) resources[key] = resources[name];
        if (persist) { try { Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!); File.WriteAllText(PreferencePath, dark ? "dark" : "light"); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    }
}
