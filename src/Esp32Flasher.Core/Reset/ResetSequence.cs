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

    public static async Task UsbJtagAsync(ISerialTransport transport, CancellationToken cancellationToken = default)
    {
        SetRts(transport, false);
        transport.Dtr = false;
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);

        transport.Dtr = true;
        SetRts(transport, false);
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);

        SetRts(transport, true);
        transport.Dtr = false;
        SetRts(transport, true);
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);

        transport.Dtr = false;
        SetRts(transport, false);
    }

    private static void SetRts(ISerialTransport transport, bool state)
    {
        transport.Rts = state;
        transport.Dtr = transport.Dtr;
    }
}
