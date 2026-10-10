using System.Collections.Concurrent;
using System.Reflection;

namespace Esp32Flasher.Core.Chips;

public static class StubBlobs
{
    private static readonly ConcurrentDictionary<string, StubBlob> Cache = new();

    public static StubBlob Load(string resourceName)
        => Cache.GetOrAdd(resourceName, LoadCore);

    private static StubBlob LoadCore(string resourceName)
    {
        Assembly assembly = typeof(StubBlobs).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded stub resource '{resourceName}' was not found.");
        }

        using var reader = new StreamReader(stream);
        return StubBlob.FromJson(reader.ReadToEnd());
    }
}
