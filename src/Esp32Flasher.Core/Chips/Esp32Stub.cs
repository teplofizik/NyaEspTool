using System.Reflection;

namespace Esp32Flasher.Core.Chips;

public static class Esp32Stub
{
    public const string ResourceName = "Esp32Flasher.Core.Resources.esp32_stub.json";

    private static readonly Lazy<StubBlob> Blob = new(LoadCore, isThreadSafe: true);

    public static StubBlob Load() => Blob.Value;

    private static StubBlob LoadCore()
    {
        Assembly assembly = typeof(Esp32Stub).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded stub resource '{ResourceName}' was not found.");
        }

        using var reader = new StreamReader(stream);
        return StubBlob.FromJson(reader.ReadToEnd());
    }
}
