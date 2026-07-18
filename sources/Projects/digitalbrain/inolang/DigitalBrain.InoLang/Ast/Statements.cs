using DigitalBrain.InoLang.Text;

namespace DigitalBrain.InoLang.Ast;

public abstract record Stmt(SourceSpan Span);
public sealed record LetAskStmt(string Var, string Port, Expr Prompt, SourceSpan Span) : Stmt(Span);
public sealed record LetExprStmt(string Var, Expr Value, SourceSpan Span) : Stmt(Span);
public sealed record EmitStmt(string Port, IReadOnlyList<NamedArg> Args, SourceSpan Span) : Stmt(Span);
public sealed record SaveStmt(Expr Value, string Port, SourceSpan Span) : Stmt(Span);
public sealed record RememberStmt(Expr Text, Expr? Value, SourceSpan Span) : Stmt(Span);
public sealed record CountStmt(string Counter, SourceSpan Span) : Stmt(Span);
public sealed record LogStmt(Expr Message, SourceSpan Span) : Stmt(Span);
public sealed record NamedArg(string Name, Expr Value);
public sealed record IfStmt(Expr Cond, IReadOnlyList<Stmt> ThenBody, IReadOnlyList<Stmt> ElseBody, SourceSpan Span) : Stmt(Span);
public sealed record ForEachStmt(string VarName, Expr SourceList, IReadOnlyList<Stmt> Body, SourceSpan Span) : Stmt(Span);
public sealed record SpeculateStmt(string Branch, IReadOnlyList<Stmt> Body, SourceSpan Span) : Stmt(Span);
public sealed record VerifyStmt(Expr Cond, SourceSpan Span) : Stmt(Span);
public sealed record ThinkStmt(Expr Goal, string? Neuron, SourceSpan Span) : Stmt(Span);
public sealed record CommitStmt(string Branch, SourceSpan Span) : Stmt(Span);
public sealed record RollbackStmt(string Branch, SourceSpan Span) : Stmt(Span);
public sealed record FlowMappingStmt(Expr Source, Expr Target, SourceSpan Span) : Stmt(Span);
public sealed record WriteStmt(Expr Target, Expr Value, SourceSpan Span) : Stmt(Span);

