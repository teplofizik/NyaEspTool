namespace Esp32Flasher.Core.Serial;

public interface ISerialTransport : IAsyncDisposable
{
    bool IsOpen { get; }

    int BaudRate { get; set; }

    bool Dtr { get; set; }

    bool Rts { get; set; }

    Task OpenAsync(CancellationToken cancellationToken = default);

    Task CloseAsync();

    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);

    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

    void DiscardInBuffer();

    void DiscardOutBuffer();
}
