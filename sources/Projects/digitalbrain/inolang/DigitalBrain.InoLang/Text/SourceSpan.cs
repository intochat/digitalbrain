namespace DigitalBrain.InoLang.Text;

public readonly record struct SourceSpan(int Start, int End)
{
    public int Length => End - Start;
    public static SourceSpan Empty => new(0, 0);
}
