using System.Text;
using System.Text.Json;
using System.IO;
using E2ETester.Core;

namespace E2ETester.Desktop;

public sealed partial class MainViewModel
{
    private bool prettyPrintResponse = true, wrapResponse = true;
    public bool PrettyPrintResponse
    {
        get => prettyPrintResponse;
        set { if (Set(ref prettyPrintResponse, value)) Changed(nameof(ResponseText)); }
    }
    public bool WrapResponse { get => wrapResponse; set => Set(ref wrapResponse, value); }

    private string DisplayResponseBody(ResponseSnapshot response)
    {
        if (!PrettyPrintResponse || response.BodyEncoding == "base64") return response.Body;
        try
        {
            using var json = JsonDocument.Parse(response.Body);
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions
            {
                Indented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            })) json.WriteTo(writer);
            return Encoding.UTF8.GetString(output.ToArray());
        }
        catch (JsonException) { return response.Body; }
    }
}
