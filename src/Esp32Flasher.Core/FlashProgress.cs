namespace Esp32Flasher.Core;

public enum FlashStage
{
    Connect,
    Sync,
    Stub,
    Erase,
    Write,
    Verify,
    Read,
    Reset,
}

public sealed record FlashProgress(
    FlashStage Stage,
    long BytesDone,
    long BytesTotal,
    string? CurrentItem = null)
{
    public double Fraction => BytesTotal <= 0 ? 0 : (double)BytesDone / BytesTotal;
}
