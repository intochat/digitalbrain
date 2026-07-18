using System.Diagnostics;

namespace DigitalBrain.InoLang.Text;

public sealed class InoSource
{
    readonly int[] _lineStarts;

    public InoSource(string text)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n') starts.Add(i + 1);
        _lineStarts = [.. starts];
    }

    public string Text { get; }

    public (int Line, int Column) LineColumn(int offset)
    {
        Debug.Assert(offset >= 0,
            $"LineColumn offset {offset} is negative — caller bug.");
        Debug.Assert(offset <= Text.Length,
            $"LineColumn offset {offset} exceeds source length {Text.Length} — caller bug.");
        if (offset < 0) offset = 0;
        if (offset > Text.Length) offset = Text.Length;
        var lo = 0;
        var hi = _lineStarts.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (_lineStarts[mid] <= offset) lo = mid;
            else hi = mid - 1;
        }
        return (lo + 1, offset - _lineStarts[lo] + 1);
    }
}
