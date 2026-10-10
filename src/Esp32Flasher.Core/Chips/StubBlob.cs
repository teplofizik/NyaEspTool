using System.Text.Json;

namespace Esp32Flasher.Core.Chips;

public sealed class StubBlob
{
    public required byte[] Text { get; init; }

    public required uint TextStart { get; init; }

    public required byte[] Data { get; init; }

    public required uint DataStart { get; init; }

    public uint? BssStart { get; init; }

    public required uint Entry { get; init; }

    public static StubBlob FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        return new StubBlob
        {
            Text = Convert.FromBase64String(root.GetProperty("text").GetString()!),
            TextStart = root.GetProperty("text_start").GetUInt32(),
            Data = Convert.FromBase64String(root.GetProperty("data").GetString()!),
            DataStart = root.GetProperty("data_start").GetUInt32(),
            BssStart = root.TryGetProperty("bss_start", out JsonElement bss) ? bss.GetUInt32() : null,
            Entry = root.GetProperty("entry").GetUInt32(),
        };
    }
}
