using DigitalBrain.InoLang.Text;

namespace DigitalBrain.InoLang.Lexing;

public readonly record struct Token(TokenKind Kind, string Text, SourceSpan Span);
