using System.Buffers.Binary;
using System.Threading.Channels;
using Esp32Flasher.Core.Protocol;
using Esp32Flasher.Core.Serial;
using Esp32Flasher.Core.Slip;

namespace Esp32Flasher.Core.Tests.Fakes;

internal sealed class FakeEspTransport : ISerialTransport
{
    private readonly Channel<byte[]> _rx = Channel.CreateUnbounded<byte[]>();
    private readonly Dictionary<EspCommand, Func<byte[], FakeResponse>> _handlers = new();
    private bool _dtr;
    private bool _rts;

    public bool IsOpen { get; private set; }

    public int BaudRate { get; set; } = 115200;

    public bool Dtr
    {
        get => _dtr;
        set
        {
            _dtr = value;
            LineChanges.Add((_dtr, _rts));
        }
    }

    public bool Rts
    {
        get => _rts;
        set
        {
            _rts = value;
            LineChanges.Add((_dtr, _rts));
        }
    }

    public List<(EspCommand Command, byte[] Data, uint Checksum)> Received { get; } = new();

    public List<(bool Dtr, bool Rts)> LineChanges { get; } = new();

    public List<byte[]> RawWrites { get; } = new();

    public void EnqueueFrame(byte[] payload) => _rx.Writer.TryWrite(SlipCodec.Encode(payload));

    public void On(EspCommand command, Func<byte[], FakeResponse> handler)
        => _handlers[command] = handler;

    public void On(EspCommand command, uint value = 0, byte[]? data = null)
        => _handlers[command] = _ => new FakeResponse(value, data ?? Array.Empty<byte>());

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        IsOpen = true;
        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        IsOpen = false;
        return Task.CompletedTask;
    }

    public async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        byte[] chunk;
        try
        {
            chunk = await _rx.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        int count = Math.Min(buffer.Length, chunk.Length);
        chunk.AsSpan(0, count).CopyTo(buffer.Span);
        return count;
    }

    public ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        RawWrites.Add(buffer.ToArray());
        ReadOnlySpan<byte> span = buffer.Span;
        while (SlipCodec.TryDecode(span, out byte[] packet, out int consumed))
        {
            span = span[consumed..];
            if (packet.Length < EspPacket.HeaderSize || packet[0] != EspPacket.DirectionRequest)
            {
                continue;
            }

            var command = (EspCommand)packet[1];
            ushort size = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2, 2));
            uint checksum = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4, 4));
            byte[] data = packet.AsSpan(EspPacket.HeaderSize, size).ToArray();
            Received.Add((command, data, checksum));

            if (_handlers.TryGetValue(command, out Func<byte[], FakeResponse>? handler))
            {
                FakeResponse response = handler(data);
                byte[] payload = BuildResponse(command, response.Value, response.Data);
                _rx.Writer.TryWrite(SlipCodec.Encode(payload));
                foreach (byte[] followUp in response.FollowUp ?? Array.Empty<byte[]>())
                {
                    _rx.Writer.TryWrite(SlipCodec.Encode(followUp));
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    public void DiscardInBuffer()
    {
    }

    public void DiscardOutBuffer()
    {
    }

    public ValueTask DisposeAsync()
    {
        _rx.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private static byte[] BuildResponse(EspCommand command, uint value, byte[] data)
    {
        byte[] payload = new byte[EspPacket.HeaderSize + data.Length];
        payload[0] = EspPacket.DirectionResponse;
        payload[1] = (byte)command;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), (ushort)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), value);
        data.CopyTo(payload.AsSpan(EspPacket.HeaderSize));
        return payload;
    }
}

internal readonly record struct FakeResponse(uint Value, byte[] Data, byte[][]? FollowUp = null);
