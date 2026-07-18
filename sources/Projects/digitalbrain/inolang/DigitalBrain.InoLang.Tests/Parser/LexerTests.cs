using DigitalBrain.InoLang.Diagnostics;
using DigitalBrain.InoLang.Lexing;

namespace DigitalBrain.InoLang.Tests.Parsing;

public sealed class LexerTests
{
    static List<Token> Lex(string s)
    {
        var bag = new DiagnosticBag();
        var toks = new Lexer(s, bag).Lex();
        bag.HasErrors.Should().BeFalse(string.Join(";", bag.Items.Select(i => i.Message)));
        return toks;
    }

    [Fact]
    public void Lexes_port_ref_vs_comment()
    {
        var k = Lex("using ask = synapse(A.B)   # inbound\n")
            .Select(t => t.Kind).ToArray();
        k.Should().Equal(
            TokenKind.Using, TokenKind.Ask, TokenKind.Equals,
            TokenKind.Synapse, TokenKind.LParen, TokenKind.Fqn, TokenKind.RParen,
            TokenKind.NewLine, TokenKind.Eof);
    }

    [Fact]
    public void Emits_indent_and_dedent()
    {
        var k = Lex("neuron A.B\n  log \"hi\"\n").Select(t => t.Kind).ToArray();
        k.Should().Equal(
            TokenKind.Neuron, TokenKind.Fqn, TokenKind.NewLine,
            TokenKind.Indent, TokenKind.Log, TokenKind.String, TokenKind.NewLine,
            TokenKind.Dedent, TokenKind.Eof);
    }

    [Fact]
    public void String_keeps_interpolation_braces_verbatim()
    {
        var t = Lex("log \"hi {x} there\"\n").Single(x => x.Kind == TokenKind.String);
        t.Text.Should().Be("hi {x} there");
    }

    [Fact]
    public void Emits_terminating_newline_when_input_has_no_trailing_newline()
    {
        var k = Lex("log \"hi\"").Select(t => t.Kind).ToArray();
        k.Should().Equal(
            TokenKind.Log, TokenKind.String, TokenKind.NewLine, TokenKind.Eof);
    }

    [Fact]
    public void Tolerates_crlf_line_endings()
    {
        var k = Lex("neuron A.B\r\n  log \"hi\"\r\n").Select(t => t.Kind).ToArray();
        k.Should().Equal(
            TokenKind.Neuron, TokenKind.Fqn, TokenKind.NewLine,
            TokenKind.Indent, TokenKind.Log, TokenKind.String, TokenKind.NewLine,
            TokenKind.Dedent, TokenKind.Eof);
    }
}
