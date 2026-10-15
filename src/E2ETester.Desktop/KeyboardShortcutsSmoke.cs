using System.Windows.Input;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Data;

namespace E2ETester.Desktop;

internal static class KeyboardShortcutsSmoke
{
    internal static async Task RunAsync(MainWindow window, MainViewModel model, string screenshot)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "e2e-shortcuts-" + Guid.NewGuid().ToString("N"), "shortcuts.json");
        var preferences = ShortcutPreferences.Defaults();
        preferences["next"] = new(Key.E, ModifierKeys.Alt);
        preferences["cancel"] = new(Key.None, ModifierKeys.None);
        ShortcutPreferences.Save(path, preferences);
        var restored = ShortcutPreferences.Load(path);
        if (restored["next"] != preferences["next"] || restored["cancel"].Key != Key.None) throw new InvalidOperationException("Custom shortcuts did not persist.");
        using (var customModel = new MainViewModel(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "workspace")))
        {
            var custom = new MainWindow(customModel, restoreDraft: false, shortcutSettingsPath: path) { Owner = window };
            custom.Show(); customModel.SelectedCase = customModel.Cases[0];
            if (custom.HandleShortcut(Key.D, ModifierKeys.Alt)) throw new InvalidOperationException("Old mapping is still active.");
            custom.HandleShortcut(Key.E, ModifierKeys.Alt);
            if (customModel.SelectedCase != customModel.Cases[1] || custom.HandleShortcut(Key.X, ModifierKeys.Alt)) throw new InvalidOperationException("Remapped or disabled action ignored.");
            custom.Close();
        }
        preferences["previous"] = preferences["next"];
        if (ShortcutPreferences.Validate(preferences) is null) throw new InvalidOperationException("Duplicate shortcuts allowed.");
        preferences = ShortcutPreferences.Defaults(); preferences["send"] = new(Key.C, ModifierKeys.Control);
        if (ShortcutPreferences.Validate(preferences) is null) throw new InvalidOperationException("Clipboard shortcut allowed.");
        preferences["send"] = new(Key.S, ModifierKeys.Control | ModifierKeys.Alt);
        if (ShortcutPreferences.Validate(preferences) is null) throw new InvalidOperationException("AltGr shortcut allowed.");
        System.IO.File.WriteAllText(path, "broken settings");
        if (ShortcutPreferences.Load(path)["send"] != ShortcutPreferences.Defaults()["send"]) throw new InvalidOperationException("Broken preferences prevented fallback.");
        var settings = new ShortcutSettingsWindow(path) { Owner = window };
        settings.Show(); settings.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)settings.ActualWidth, (int)settings.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(settings);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var image = System.IO.File.Create(screenshot + ".shortcuts.png")) encoder.Save(image);
        settings.Close();
        var original = model.SelectedCase;
        model.SelectedCase = model.Cases[0];
        if (window.HandleShortcut(Key.Enter, ModifierKeys.None) || window.HandleShortcut(Key.S, ModifierKeys.Control | ModifierKeys.Shift))
            throw new InvalidOperationException("Shortcut swallowed ordinary editing or a different modifier combination.");
        window.HandleShortcut(Key.A, ModifierKeys.Alt);
        if (model.SelectedCase != model.Cases[0]) throw new InvalidOperationException("Disabled Previous changed case.");
        window.HandleShortcut(Key.D, ModifierKeys.Alt, repeat: true);
        if (model.SelectedCase != model.Cases[0]) throw new InvalidOperationException("Held shortcut repeated navigation.");
        window.HandleShortcut(Key.D, ModifierKeys.Alt);
        if (model.SelectedCase != model.Cases[1]) throw new InvalidOperationException("Next shortcut did not navigate.");
        window.HandleShortcut(Key.A, ModifierKeys.Alt);
        if (model.SelectedCase != model.Cases[0]) throw new InvalidOperationException("Previous shortcut did not navigate.");
        var attempts = model.Attempts.Count;
        window.HandleShortcut(Key.S, ModifierKeys.Alt, repeat: true);
        if (model.Attempts.Count != attempts) throw new InvalidOperationException("Held Send created an attempt.");
        // Exercise deferred editor binding before dispatch (the same path used by Send).
        var text = window.CaseInstructions;
        var before = model.SelectedCase!.Instructions;
        text.SetBinding(TextBox.TextProperty, new Binding("SelectedCase.Instructions") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit });
        window.Activate(); text.Focus(); Keyboard.Focus(text);
        text.Text = "Unsaved keyboard edit";
        if (model.SelectedCase.Instructions != before) throw new InvalidOperationException("Test needs a pending binding.");
        window.HandleShortcut(Key.D, ModifierKeys.Alt);
        if (model.Cases[0].Instructions != "Unsaved keyboard edit") throw new InvalidOperationException("Shortcut lost the active edit.");
        model.Cases[0].Instructions = before;
        text.SetBinding(TextBox.TextProperty, new Binding("SelectedCase.Instructions") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        if (!window.NextCaseButton.ToolTip.ToString()!.Contains("Alt+D")) throw new InvalidOperationException("Shortcut hint missing.");
        model.SelectedCase = original;
        await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
