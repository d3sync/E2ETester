using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace E2ETester.Desktop;

// Renders the headings, paragraphs, code spans, links and table emitted by
// generate_dependency_notices.py. No browser or remote content is needed.
internal static class NoticesDocument
{
    public static RichTextBox Create(string markdown)
    {
        var document = new FlowDocument { PagePadding = new Thickness(12), FontFamily = new FontFamily("Segoe UI"), FontSize = 14 };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "Ink");
        TableRowGroup? rows = null;
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) { rows = null; continue; }
            if (line.StartsWith('|'))
            {
                var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
                if (cells.All(cell => Regex.IsMatch(cell, @"^:?-+:?$"))) continue;
                var header = rows is null;
                if (rows is null)
                {
                    var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 8, 0, 12) };
                    foreach (var width in new[] { 3d, 2d, 1d }) table.Columns.Add(new TableColumn { Width = new GridLength(width, GridUnitType.Star) });
                    rows = new TableRowGroup(); table.RowGroups.Add(rows); document.Blocks.Add(table);
                }
                var row = new TableRow();
                foreach (var value in cells)
                {
                    var cell = new TableCell(Paragraph(value)) { Padding = new Thickness(8), BorderThickness = new Thickness(0, 0, 0, 1) };
                    cell.SetResourceReference(TableCell.BorderBrushProperty, "Line");
                    if (header) { cell.FontWeight = FontWeights.SemiBold; cell.SetResourceReference(TableCell.BackgroundProperty, "Canvas"); }
                    row.Cells.Add(cell);
                }
                rows.Rows.Add(row);
                continue;
            }
            rows = null;
            var heading = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            var paragraph = Paragraph(heading.Success ? heading.Groups[2].Value : line);
            paragraph.Margin = new Thickness(0, 0, 0, 12);
            if (heading.Success) { paragraph.FontSize = heading.Groups[1].Length == 1 ? 22 : 18; paragraph.FontWeight = FontWeights.SemiBold; }
            document.Blocks.Add(paragraph);
        }
        var viewer = new RichTextBox
        {
            Document = document, IsReadOnly = true, IsDocumentEnabled = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0), Margin = new Thickness(0, 12, 0, 0)
        };
        viewer.SetResourceReference(Control.BackgroundProperty, "Surface");
        viewer.SetResourceReference(Control.ForegroundProperty, "Ink");
        return viewer;
    }

    private static Paragraph Paragraph(string text)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        var offset = 0;
        foreach (Match match in Regex.Matches(text, @"`([^`]+)`|\[([^\]]+)\]\((https?://[^\s)]+)\)"))
        {
            paragraph.Inlines.Add(new Run(text[offset..match.Index]));
            if (match.Groups[1].Success)
            {
                var code = new Run(match.Groups[1].Value) { FontFamily = new FontFamily("Consolas") };
                code.SetResourceReference(TextElement.BackgroundProperty, "Canvas"); paragraph.Inlines.Add(code);
            }
            else if (Uri.TryCreate(match.Groups[3].Value, UriKind.Absolute, out var uri))
            {
                var link = new Hyperlink(new Run(match.Groups[2].Value)) { NavigateUri = uri, ToolTip = uri.AbsoluteUri };
                link.SetResourceReference(TextElement.ForegroundProperty, "Link");
                link.RequestNavigate += (_, e) =>
                {
                    try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                    { MessageBox.Show("Could not open your browser. You can copy this address:\n" + e.Uri.AbsoluteUri, "Package link"); }
                    e.Handled = true;
                };
                paragraph.Inlines.Add(link);
            }
            else paragraph.Inlines.Add(new Run(match.Value));
            offset = match.Index + match.Length;
        }
        paragraph.Inlines.Add(new Run(text[offset..]));
        return paragraph;
    }
}
