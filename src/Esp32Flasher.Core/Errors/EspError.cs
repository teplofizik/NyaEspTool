namespace Esp32Flasher.Core.Errors;

public enum EspError
{
    PortNotFound,
    PortBusy,
    NoSync,
    ChipNotSupported,
    CommandFailed,
    RomError,
    StubError,
    ChecksumMismatch,
    FlashWriteFailed,
    VerificationFailed,
    Timeout,
    Cancelled,
}
