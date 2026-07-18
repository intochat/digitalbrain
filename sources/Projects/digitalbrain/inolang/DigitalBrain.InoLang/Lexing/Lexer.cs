using DigitalBrain.InoLang.Diagnostics;
using DigitalBrain.InoLang.Text;

namespace DigitalBrain.InoLang.Lexing;

public sealed class Lexer
{
    readonly string text;
    readonly DiagnosticBag diagnostics;

    public Lexer(string source, DiagnosticBag diag)
    {
        // InoLang is line-oriented on '\n'. Normalize CRLF/CR so documents
        // authored on Windows or checked out via git autocrlf lex correctly.
        text = source.AsSpan().IndexOf('\r') < 0
            ? source
            : source.Replace("\r\n", "\n").Replace('\r', '\n');
        diagnostics = diag;
    }

    static readonly Dictionary<string, TokenKind> Keywords = new(StringComparer.Ordinal)
    {
        ["neuron"] = TokenKind.Neuron, ["using"] = TokenKind.Using,
        ["synapse"] = TokenKind.Synapse, ["signal"] = TokenKind.Synapse,
        ["on"] = TokenKind.On, ["where"] = TokenKind.Where, ["is"] = TokenKind.Is,
        ["let"] = TokenKind.Let, ["ask"] = TokenKind.Ask, ["to"] = TokenKind.To,
        ["for"] = TokenKind.For, ["emit"] = TokenKind.Emit, ["save"] = TokenKind.Save,
        ["into"] = TokenKind.Into, ["remember"] = TokenKind.Remember,
        ["count"] = TokenKind.Count, ["log"] = TokenKind.Log,
        ["activated"] = TokenKind.Activated, ["deactivated"] = TokenKind.Deactivated,
        ["created"] = TokenKind.Created, ["scenario"] = TokenKind.Scenario,
        ["given"] = TokenKind.Given, ["when"] = TokenKind.When, ["then"] = TokenKind.Then,
        ["and"] = TokenKind.And, ["returns"] = TokenKind.Returns, ["has"] = TokenKind.Has,
        ["emitted"] = TokenKind.Emitted, ["with"] = TokenKind.With,
        ["counter"] = TokenKind.Counter,
        ["it"] = TokenKind.It, ["if"] = TokenKind.If, ["else"] = TokenKind.Else,
        ["foreach"] = TokenKind.ForEach, ["in"] = TokenKind.In, ["recall"] = TokenKind.Recall,
        ["speculate"] = TokenKind.Speculate, ["verify"] = TokenKind.Verify,
        ["think"] = TokenKind.Think, ["commit"] = TokenKind.Commit,
        ["rollback"] = TokenKind.Rollback, ["failure"] = TokenKind.Failure,
        ["ui"] = TokenKind.Ui, ["mock"] = TokenKind.Mock, ["expect"] = TokenKind.Expect,
        ["write"] = TokenKind.Write, ["test"] = TokenKind.Test,
    };

    int _pos;
    readonly List<int> _indents = [0];
    readonly List<Token> _out = [];

    public List<Token> Lex()
    {
        while (_pos < text.Length)
            LexLine();
        if (_out.Count > 0
            && _out[^1].Kind != TokenKind.NewLine
            && _out[^1].Kind != TokenKind.Indent
            && _out[^1].Kind != TokenKind.Dedent)
            Add(TokenKind.NewLine, "");
        while (_indents.Count > 1) { _indents.RemoveAt(_indents.Count - 1); Add(TokenKind.Dedent, ""); }
        Add(TokenKind.Eof, "");
        return _out;
    }

    void LexLine()
    {
        var lineStart = _pos;
        var indent = 0;
        while (_pos < text.Length && text[_pos] == ' ') { _pos++; indent++; }

        if (_pos >= text.Length || text[_pos] == '\n' ||
            (text[_pos] == '#' && IsCommentHash(_pos)))
        {
            SkipToEol();
            return;
        }

        EmitIndentation(indent, lineStart);

        while (_pos < text.Length && text[_pos] != '\n')
        {
            var c = text[_pos];
            if (c == ' ') { _pos++; continue; }
            if (c == '#' && IsCommentHash(_pos)) { SkipToEol(); return; }
            if (LexToken(c)) continue;
            diagnostics.Error("INO100", $"Unexpected character '{c}'.", new SourceSpan(_pos, _pos + 1));
            _pos++;
        }
        SkipToEol();
    }

    bool IsCommentHash(int at)
    {
        var n = at + 1;
        return n >= text.Length || text[n] == '\n' || text[n] == ' ' || text[n] == '\t';
    }

    void SkipToEol()
    {
        while (_pos < text.Length && text[_pos] != '\n') _pos++;
        if (_pos < text.Length)
        {
            if (_out.Count > 0 && _out[^1].Kind != TokenKind.NewLine &&
                _out[^1].Kind != TokenKind.Indent && _out[^1].Kind != TokenKind.Dedent)
                Add(TokenKind.NewLine, "\n", _pos, _pos + 1);
            _pos++;
        }
    }

    void EmitIndentation(int indent, int lineStart)
    {
        var cur = _indents[^1];
        if (indent > cur) { _indents.Add(indent); Add(TokenKind.Indent, "", lineStart, _pos); }
        else
            while (indent < _indents[^1])
            {
                _indents.RemoveAt(_indents.Count - 1);
                Add(TokenKind.Dedent, "", lineStart, _pos);
            }
    }

    bool LexToken(char c)
    {
        var start = _pos;
        switch (c)
        {
            case '(': _pos++; Add(TokenKind.LParen, "(", start, _pos); return true;
            case ')': _pos++; Add(TokenKind.RParen, ")", start, _pos); return true;
            case '[': _pos++; Add(TokenKind.LBracket, "[", start, _pos); return true;
            case ']': _pos++; Add(TokenKind.RBracket, "]", start, _pos); return true;
            case ',': _pos++; Add(TokenKind.Comma, ",", start, _pos); return true;
            case '{': _pos++; Add(TokenKind.LBrace, "{", start, _pos); return true;
            case '}': _pos++; Add(TokenKind.RBrace, "}", start, _pos); return true;
            case '.': _pos++; Add(TokenKind.Dot, ".", start, _pos); return true;
            case ':': _pos++; Add(TokenKind.Colon, ":", start, _pos); return true;
            case '=':
                if (Peek(1) == '=') { _pos += 2; Add(TokenKind.EqEq, "==", start, _pos); return true; }
                _pos++; Add(TokenKind.Equals, "=", start, _pos); return true;
            case '-':
                if (Peek(1) == '>') { _pos += 2; Add(TokenKind.Arrow, "->", start, _pos); return true; }
                if (char.IsDigit(Peek(1))) { _pos++; return LexNumber(start); }
                break;
            case '>':
                if (Peek(1) == '=') { _pos += 2; Add(TokenKind.GreaterThanOrEqual, ">=", start, _pos); return true; }
                _pos++; Add(TokenKind.GreaterThan, ">", start, _pos); return true;
            case '<':
                if (Peek(1) == '=') { _pos += 2; Add(TokenKind.LessThanOrEqual, "<=", start, _pos); return true; }
                _pos++; Add(TokenKind.LessThan, "<", start, _pos); return true;
            case '"': return LexString(start);
            case '@': return LexTelemetry(start);
        }
        if (char.IsLetter(c)) return LexWord(start);
        if (char.IsDigit(c)) return LexNumber(start);
        return false;
    }

    char Peek(int d) => _pos + d < text.Length ? text[_pos + d] : '\0';

    bool LexString(int start)
    {
        _pos++;
        var contentStart = _pos;
        while (_pos < text.Length && text[_pos] != '"' && text[_pos] != '\n')
            _pos++;
        if (_pos >= text.Length || text[_pos] != '"')
        {
            diagnostics.Error("INO101", "Unterminated string.", new SourceSpan(start, _pos));
            return true;
        }
        var value = text[contentStart.._pos];
        _pos++;
        Add(TokenKind.String, value, start, _pos);
        return true;
    }

    bool LexTelemetry(int start)
    {
        _pos++; // consume the leading '@'
        while (_pos < text.Length && (char.IsLetterOrDigit(text[_pos]) ||
               text[_pos] is ':' or '_')) _pos++;
        Add(TokenKind.TelemetryTag, text[start.._pos], start, _pos);
        return true;
    }

    bool LexNumber(int start)
    {
        while (_pos < text.Length && char.IsDigit(text[_pos])) _pos++;
        // Fractional part: only consume the '.' when a digit follows, so integer
        // numbers and FQN/field-access dots are unaffected.
        if (_pos + 1 < text.Length && text[_pos] == '.' && char.IsDigit(text[_pos + 1]))
        {
            _pos++;
            while (_pos < text.Length && char.IsDigit(text[_pos])) _pos++;
        }
        Add(TokenKind.Number, text[start.._pos], start, _pos);
        return true;
    }

    bool LexWord(int start)
    {
        while (_pos < text.Length &&
               (char.IsLetterOrDigit(text[_pos]) || text[_pos] is '_' or '-')) _pos++;
        if (_pos < text.Length && text[_pos] == '.' && _pos + 1 < text.Length &&
            char.IsLetter(text[_pos + 1]))
        {
            while (_pos < text.Length &&
                   (char.IsLetterOrDigit(text[_pos]) || text[_pos] is '.' or '_' or '-')) _pos++;
            Add(TokenKind.Fqn, text[start.._pos], start, _pos);
            return true;
        }
        var word = text[start.._pos];
        Add(Keywords.TryGetValue(word, out var kw) ? kw : TokenKind.Ident,
            word, start, _pos);
        return true;
    }

    void Add(TokenKind kind, string txt) => Add(kind, txt, _pos, _pos);
    void Add(TokenKind kind, string txt, int s, int e)
        => _out.Add(new Token(kind, txt, new SourceSpan(s, e)));
}
