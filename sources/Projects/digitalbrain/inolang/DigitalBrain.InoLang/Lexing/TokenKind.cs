namespace DigitalBrain.InoLang.Lexing;

public enum TokenKind
{
    // structure
    NewLine, Indent, Dedent, Eof,
    // punctuation
    LParen, RParen, LBracket, RBracket, Equals, Colon, Comma, Dot, EqEq,
    // literals + names
    Ident, Fqn, String, Number, TelemetryTag,
    // keywords
    Neuron, Using, Synapse, NeuronKw, On, Where, Is, Let, Ask, To, For,
    Emit, Save, Into, Remember, Count, Log,
    Activated, Deactivated, Created,
    Scenario, Given, When, Then, And, Returns, Has, Emitted, With, Counter,
    It, If, Else, ForEach, In, Recall,
    Speculate, Verify, Think, Commit, Rollback, Failure,
    Ui, GreaterThan, LessThan, GreaterThanOrEqual, LessThanOrEqual,
    Arrow, Mock, Expect, Write, Test,
    // braces — only meaningful inside ui: data literals ({lat,lng} maps); appended
    // at the end so the IsIdent enum-range checks in the parser stay valid.
    LBrace, RBrace
}
