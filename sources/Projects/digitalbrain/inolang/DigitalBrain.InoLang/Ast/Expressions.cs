using DigitalBrain.InoLang.Text;

namespace DigitalBrain.InoLang.Ast;

public abstract record Expr(SourceSpan Span);
public sealed record StringExpr(string Value, SourceSpan Span) : Expr(Span);
public sealed record NumberExpr(long Value, SourceSpan Span) : Expr(Span);
public sealed record PortRefExpr(string Name, SourceSpan Span) : Expr(Span);
public sealed record FieldAccessExpr(string PortName, string Field, SourceSpan Span) : Expr(Span);
public sealed record CallExpr(string Builtin, Expr Arg, SourceSpan Span) : Expr(Span);
public sealed record InterpExpr(IReadOnlyList<Expr> Parts, SourceSpan Span) : Expr(Span);
public sealed record RecallExpr(Expr Text, SourceSpan Span) : Expr(Span);
public sealed record ArgsExpr(IReadOnlyList<NamedArg> Args, SourceSpan Span) : Expr(Span);
