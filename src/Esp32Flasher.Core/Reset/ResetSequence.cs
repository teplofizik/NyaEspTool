using Esp32Flasher.Core.Serial;

namespace Esp32Flasher.Core.Reset;

public static class ResetSequence
{
    public static async Task ClassicAsync(
        ISerialTransport transport,
        TimeSpan resetDelay,
        CancellationToken cancellationToken = default)
    {
        transport.Dtr = false;
        transport.Rts = true;
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);

        transport.Dtr = true;
        transport.Rts = false;
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);

        transport.Dtr = false;
        await Task.Delay(resetDelay, cancellationToken).ConfigureAwait(false);
    }

    public static async Task HardAsync(
        ISerialTransport transport,
        TimeSpan holdDelay,
        TimeSpan postReleaseDelay,
        CancellationToken cancellationToken = default)
    {
        transport.Rts = true;
        await Task.Delay(holdDelay, cancellationToken).ConfigureAwait(false);

        transport.Rts = false;
        await Task.Delay(postReleaseDelay, cancellationToken).ConfigureAwait(false);
    }
}
