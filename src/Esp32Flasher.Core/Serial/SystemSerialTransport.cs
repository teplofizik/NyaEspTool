using System.IO.Ports;

namespace Esp32Flasher.Core.Serial;

public sealed class SystemSerialTransport : ISerialTransport
{
    private readonly string _portName;
    private readonly object _sync = new();
    private SerialPort? _port;
    private int _baudRate;

    public SystemSerialTransport(string portName, int baudRate = 115200)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new ArgumentException("Port name is required.", nameof(portName));
        }

        _portName = portName;
        _baudRate = baudRate;
    }

    public bool IsOpen
    {
        get
        {
            lock (_sync)
            {
                return _port?.IsOpen ?? false;
            }
        }
    }

    public int BaudRate
    {
        get => _baudRate;
        set
        {
            _baudRate = value;
            lock (_sync)
            {
                if (_port is { IsOpen: true })
                {
                    _port.BaudRate = value;
                }
            }
        }
    }

    public bool Dtr
    {
        get
        {
            lock (_sync)
            {
                return _port?.DtrEnable ?? false;
            }
        }
        set
        {
            lock (_sync)
            {
                if (_port is not null)
                {
                    _port.DtrEnable = value;
                }
            }
        }
    }

    public bool Rts
    {
        get
        {
            lock (_sync)
            {
                return _port?.RtsEnable ?? false;
            }
        }
        set
        {
            lock (_sync)
            {
                if (_port is not null)
                {
                    _port.RtsEnable = value;
                }
            }
        }
    }

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_port is { IsOpen: true })
            {
                return Task.CompletedTask;
            }

            _port = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false,
                ReadTimeout = SerialPort.InfiniteTimeout,
                WriteTimeout = 5000,
            };
            _port.Open();
        }

        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        SerialPort? port;
        lock (_sync)
        {
            port = _port;
            _port = null;
        }

        if (port is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            if (port.IsOpen)
            {
                port.Close();
            }
        }
        finally
        {
            port.Dispose();
        }

        return Task.CompletedTask;
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        SerialPort port = _port ?? throw new InvalidOperationException("Serial port is not open.");
        return port.BaseStream.ReadAsync(buffer, cancellationToken);
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        SerialPort port = _port ?? throw new InvalidOperationException("Serial port is not open.");
        return port.BaseStream.WriteAsync(buffer, cancellationToken);
    }

    public void DiscardInBuffer()
    {
        SerialPort? port = _port;
        if (port is { IsOpen: true })
        {
            try
            {
                port.DiscardInBuffer();
            }
            catch (IOException)
            {
            }
        }
    }

    public void DiscardOutBuffer()
    {
        SerialPort? port = _port;
        if (port is { IsOpen: true })
        {
            try
            {
                port.DiscardOutBuffer();
            }
            catch (IOException)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
