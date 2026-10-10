using System.IO.Ports;

namespace Esp32Flasher.Core.Serial;

public static class SerialPorts
{
    public static IReadOnlyList<SerialPortInfo> List()
    {
        string[] names = SerialPort.GetPortNames();
        Array.Sort(names, StringComparer.OrdinalIgnoreCase);
        return Array.ConvertAll(names, static name => new SerialPortInfo(name));
    }
}
