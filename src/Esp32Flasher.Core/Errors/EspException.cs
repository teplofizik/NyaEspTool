namespace Esp32Flasher.Core.Errors;

public sealed class EspException : Exception
{
    public EspException(EspError error, string message)
        : base(message)
    {
        Error = error;
    }

    public EspException(EspError error, string message, Exception innerException)
        : base(message, innerException)
    {
        Error = error;
    }

    public EspError Error { get; }
}
