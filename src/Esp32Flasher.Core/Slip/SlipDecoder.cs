namespace Esp32Flasher.Core.Slip;

public sealed class SlipDecoder
{
    private readonly List<byte> _frame = new();
    private bool _inFrame;
    private bool _escaped;

    public IReadOnlyList<byte[]> Feed(ReadOnlySpan<byte> data)
    {
        List<byte[]>? frames = null;

        foreach (byte value in data)
        {
            if (value == SlipCodec.End)
            {
                if (_inFrame && _frame.Count > 0)
                {
                    (frames ??= new List<byte[]>()).Add(_frame.ToArray());
                }

                _frame.Clear();
                _escaped = false;
                _inFrame = true;
                continue;
            }

            if (!_inFrame)
            {
                continue;
            }

            if (_escaped)
            {
                _frame.Add(value switch
                {
                    SlipCodec.EscEnd => SlipCodec.End,
                    SlipCodec.EscEsc => SlipCodec.Esc,
                    _ => value,
                });
                _escaped = false;
                continue;
            }

            if (value == SlipCodec.Esc)
            {
                _escaped = true;
                continue;
            }

            _frame.Add(value);
        }

        return (IReadOnlyList<byte[]>?)frames ?? Array.Empty<byte[]>();
    }

    public void Reset()
    {
        _frame.Clear();
        _inFrame = false;
        _escaped = false;
    }
}
