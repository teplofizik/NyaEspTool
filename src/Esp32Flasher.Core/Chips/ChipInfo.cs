namespace Esp32Flasher.Core.Chips;

public sealed record ChipInfo(
    string Name,
    int RevisionMajor,
    int RevisionMinor,
    byte[] Mac)
{
    public string MacAddress => string.Join(':', Mac.Select(static b => b.ToString("X2")));

    public override string ToString() => $"{Name} (revision v{RevisionMajor}.{RevisionMinor})";
}
