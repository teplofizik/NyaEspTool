using Esp32Flasher.Core.Chips;
using Esp32Flasher.Core.Loaders;
using Esp32Flasher.Core.Serial;

namespace Esp32Flasher.Core;

public sealed class Esp32Connection : IAsyncDisposable
{
    private readonly EspLoader _loader;
    private readonly ISerialTransport _transport;
    private readonly bool _ownsTransport;

    private Esp32Connection(EspLoader loader, ISerialTransport transport, bool ownsTransport)
    {
        _loader = loader;
        _transport = transport;
        _ownsTransport = ownsTransport;
    }

    public ChipInfo Chip => _loader.Chip
        ?? throw new InvalidOperationException("The chip has not been detected yet.");

    public bool StubRunning => _loader.IsStub;

    public event Action<EspTraceEntry>? Traced
    {
        add => _loader.Traced += value;
        remove => _loader.Traced -= value;
    }

    public static async Task<Esp32Connection> OpenAsync(
        EspConnectionOptions options, CancellationToken cancellationToken = default)
    {
        bool ownsTransport = options.Transport is null;
        ISerialTransport transport = options.Transport ?? CreateTransport(options);
        var loader = new EspLoader(transport, options);

        try
        {
            await loader.StartAsync(cancellationToken).ConfigureAwait(false);

            if (options.LoadStub)
            {
                await loader.LoadStubAsync(
                        StubBlobs.Load(loader.ChipConfig.StubResourceName), cancellationToken)
                    .ConfigureAwait(false);

                if (options.FlashBaud is int flashBaud && flashBaud != options.InitialBaud)
                {
                    await loader.ChangeBaudAsync(flashBaud, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            loader.Stop();
            if (ownsTransport)
            {
                await transport.DisposeAsync().ConfigureAwait(false);
            }

            await loader.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new Esp32Connection(loader, transport, ownsTransport);
    }

    public Task<uint> ReadRegisterAsync(uint address, CancellationToken cancellationToken = default)
        => _loader.ReadRegAsync(address, cancellationToken);

    public Task<uint> ReadFlashIdAsync(CancellationToken cancellationToken = default)
        => _loader.ReadFlashIdAsync(cancellationToken);

    public async Task<uint?> DetectFlashSizeAsync(CancellationToken cancellationToken = default)
    {
        uint flashId = await _loader.ReadFlashIdAsync(cancellationToken).ConfigureAwait(false);
        return Esp32Chip.DecodeFlashSizeBytes(flashId);
    }

    public async Task FlashAsync(
        IReadOnlyList<FlashImage> images,
        FlashOptions options,
        IProgress<FlashProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (images.Count == 0)
        {
            throw new ArgumentException("At least one image is required.", nameof(images));
        }

        await _loader.SetSpiParamsAsync(options.FlashSizeBytes, cancellationToken).ConfigureAwait(false);

        foreach (FlashImage image in images)
        {
            FlashImage effective = image;
            if (image.Offset == _loader.ChipConfig.BootloaderOffset)
            {
                byte[] patched = Imaging.ImageFlashParams.Apply(
                    image.Data, image.Offset, options.Mode, options.Frequency, options.Size,
                    _loader.ChipConfig.BootloaderOffset);
                if (!ReferenceEquals(patched, image.Data))
                {
                    effective = image with { Data = patched };
                }
            }

            await _loader.WriteFlashAsync(effective, options, progress, cancellationToken).ConfigureAwait(false);
        }

        await _loader.FlashFinishAsync(reboot: false, cancellationToken).ConfigureAwait(false);

        if (options.Reboot)
        {
            progress?.Report(new FlashProgress(FlashStage.Reset, 0, 0));
            await _loader.HardResetAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task HardResetAsync(CancellationToken cancellationToken = default)
        => _loader.HardResetAsync(cancellationToken);

    public Task EraseFlashAsync(CancellationToken cancellationToken = default)
        => _loader.EraseFlashAsync(cancellationToken);

    public Task EraseRegionAsync(uint offset, uint size, CancellationToken cancellationToken = default)
        => _loader.EraseRegionAsync(offset, size, cancellationToken);

    public Task<byte[]> ReadFlashAsync(
        uint offset,
        uint length,
        IProgress<FlashProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => _loader.ReadFlashAsync(offset, length, progress, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _loader.Stop();
        if (_ownsTransport)
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
        }

        await _loader.DisposeAsync().ConfigureAwait(false);
    }

    private static ISerialTransport CreateTransport(EspConnectionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.PortName))
        {
            throw new ArgumentException(
                "Either Transport or PortName must be provided.", nameof(options));
        }

        return new SystemSerialTransport(options.PortName, options.InitialBaud);
    }
}
