using System.IO;
using System.Windows;
using System.Windows.Controls;
using E2ETester.Core;
using Microsoft.Win32;

namespace E2ETester.Desktop;
public partial class RequestBodyView : UserControl
{
    public RequestBodyView() => InitializeComponent();
    private void AddField(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel { Busy: false, SelectedCase: { } selected }) selected.Parts.Add(new()); }
    private void AddFile(object sender, RoutedEventArgs e) => Load(true);
    private void LoadBinary(object sender, RoutedEventArgs e) => Load(false);
    private void Load(bool multipart)
    {
        if (DataContext is not MainViewModel { Busy: false, SelectedCase: { } selected } model) return;
        var dialog = new OpenFileDialog(); if (dialog.ShowDialog() != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 1024 * 1024) throw new InvalidDataException("Choose a file of 1 MB or less.");
            var value = Convert.ToBase64String(File.ReadAllBytes(dialog.FileName));
            if (multipart) selected.Parts.Add(new BodyPart { Name = "file", FileName = Path.GetFileName(dialog.FileName), Binary = true, Value = value });
            else { selected.Body = value; selected.ContentType = "application/octet-stream"; }
            selected.SendBody = true;
            selected.BodyKind = multipart ? RequestBodyKind.Multipart : RequestBodyKind.Binary;
            model.Message = multipart ? "File added to the multipart request." : "File loaded as a binary request body.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { model.Message = "File could not be loaded; check its size and access permissions."; }
    }
}
