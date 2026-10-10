namespace Esp32Flasher.Core.Serial;

public sealed record SerialPortInfo(string PortName, string? Description = null, string? HardwareId = null);
