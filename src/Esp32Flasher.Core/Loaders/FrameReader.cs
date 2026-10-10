using System.Threading.Channels;
using Esp32Flasher.Core.Errors;
using Esp32Flasher.Core.Serial;
using Esp32Flasher.Core.Slip;

namespace Esp32Flasher.Core.Loaders;

public sealed class FrameReader : IAsyncDisposable
{
    private readonly ISerialTransport _transport;
    private readonly SlipDecoder _decoder = new();
    private readonly Channel<byte[]> _frames = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private CancellationTokenSource? _cts;
    private Task? _pump;

    public FrameReader(ISerialTransport transport)
    {
        _transport = transport;
    }

    public event Action<ReadOnlyMemory<byte>>? RawReceived;

    public Exception? Fault { get; private set; }

    public void Start()
    {
        if (_pump is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _pump = Task.Run(() => PumpAsync(_cts.Token));
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4096];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int read = await _transport.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    continue;
                }

                RawReceived?.Invoke(buffer.AsMemory(0, read));
                foreach (byte[] frame in _decoder.Feed(buffer.AsSpan(0, read)))
                {
                    _frames.Writer.TryWrite(frame);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Fault = ex;
        }
        finally
        {
            _frames.Writer.TryComplete();
        }
    }

    public async Task<byte[]?> ReadFrameAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            return await _frames.Reader.ReadAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (ChannelClosedException) when (Fault is not null)
        {
            throw new EspException(EspError.CommandFailed, $"Serial read failed: {Fault.Message}", Fault);
        }
    }

    public void Clear()
    {
        while (_frames.Reader.TryRead(out _))
        {
        }
    }

    public void Reset()
    {
        Clear();
        _decoder.Reset();
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_pump is not null)
        {
            try
            {
                await Task.WhenAny(_pump, Task.Delay(500)).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        _cts?.Dispose();
        _cts = null;
        _pump = null;
    }
}
