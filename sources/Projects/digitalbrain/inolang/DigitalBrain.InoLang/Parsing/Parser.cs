using DigitalBrain.InoLang.Ast;
using DigitalBrain.InoLang.Diagnostics;
using DigitalBrain.InoLang.Lexing;
using DigitalBrain.InoLang.Text;

namespace DigitalBrain.InoLang.Parsing;

public sealed class Parser(IReadOnlyList<Token> tokens, DiagnosticBag diagnostics)
{
    int _i;

    Token Cur => tokens[Math.Min(_i, tokens.Count - 1)];
    Token Next => tokens[Math.Min(_i + 1, tokens.Count - 1)];
    Token Peek(int ahead) => tokens[Math.Min(_i + ahead, tokens.Count - 1)];
    bool Is(TokenKind k) => Cur.Kind == k;
    Token Advance() => tokens[Math.Min(_i++, tokens.Count - 1)];

    Token Expect(TokenKind k, string code, string what)
    {
        if (Cur.Kind == k) return Advance();
        diagnostics.Error(code, $"Expected {what} but found '{Cur.Text}' ({Cur.Kind}).", Cur.Span);
        return Cur;
    }

    bool IsIdent(TokenKind k) => k == TokenKind.Ident || (k >= TokenKind.Neuron && k <= TokenKind.Failure) || k == TokenKind.Ui || (k >= TokenKind.Mock && k <= TokenKind.Test);

    Token ExpectIdent(string code, string what)
    {
        if (IsIdent(Cur.Kind)) return Advance();
        diagnostics.Error(code, $"Expected {what} but found '{Cur.Text}' ({Cur.Kind}).", Cur.Span);
        return Cur;
    }

    void SkipNewLines() { while (Is(TokenKind.NewLine)) Advance(); }

    public NeuronDoc? ParseDocument()
    {
        SkipNewLines();
        var start = Cur.Span;
        string fqn = "Inferred.Neuron";
        string? intent = null;

        if (Is(TokenKind.Neuron))
        {
            Advance();
            fqn = Expect(TokenKind.Fqn, "INO201", "a neuron FQN").Text;
            Expect(TokenKind.NewLine, "INO202", "end of line");
            Expect(TokenKind.Indent, "INO203", "an indented neuron body");
            if (Is(TokenKind.String)) { intent = Advance().Text; Expect(TokenKind.NewLine, "INO202", "end of line"); }
        }
        else
        {
            diagnostics.Error("INO200", "A document must start with 'neuron <FQN>'.", Cur.Span);
            return null;
        }

        var usings = new List<UsingDecl>();
        var counters = new List<string>();
        var states = new List<StateDecl>();
        UiDecl? ui = null;

        while (Is(TokenKind.Using) || Is(TokenKind.TelemetryTag) || (Is(TokenKind.Ident) && Cur.Text == "state"))
        {
            if (Is(TokenKind.TelemetryTag)) { ParseTelemetry(counters); continue; }
            if (Is(TokenKind.Ident) && Cur.Text == "state") { states.Add(ParseState()); continue; }
            usings.Add(ParseUsing());
        }

        var handlers = new List<Handler>();
        while (Is(TokenKind.On) || Is(TokenKind.Ui))
        {
            if (Is(TokenKind.Ui))
            {
                if (ui != null) diagnostics.Error("INO308", "Only one ui block is allowed.", Cur.Span);
                ui = ParseUi();
                SkipNewLines();
                continue;
            }
            handlers.Add(ParseHandler());
        }

        Expect(TokenKind.Dedent, "INO204", "end of neuron body");

        var scenarios = new List<ScenarioDecl>();
        SkipNewLines();
        while (Is(TokenKind.Scenario) || Is(TokenKind.Test)) { scenarios.Add(ParseScenario()); SkipNewLines(); }

        return new NeuronDoc(fqn, intent, usings, counters, handlers, scenarios, ui,
            new SourceSpan(start.Start, Cur.Span.End), states);
    }

    void ParseTelemetry(List<string> counters)
    {
        var tag = Advance().Text;
        var parts = tag.Split(':');
        if (parts.Length == 3 && parts[1] == "counter") counters.Add(parts[2]);
        Expect(TokenKind.NewLine, "INO202", "end of line");
    }

    StateDecl ParseState()
    {
        var kw = Advance(); // consume 'state'
        var name = ExpectIdent("INO211", "a state variable name").Text;
        Expect(TokenKind.Colon, "INO212", "':'");
        var type = ExpectIdent("INO213", "a state type name").Text;
        Expect(TokenKind.NewLine, "INO202", "end of line");
        return new StateDecl(name, type, kw.Span);
    }

    UsingDecl ParseUsing()
    {
        var kw = Advance();
        var name = ExpectIdent("INO211", "a port name").Text;
        Expect(TokenKind.Equals, "INO212", "'='");
        var kind = Cur.Kind switch
        {
            TokenKind.Synapse => PortKind.Synapse,
            TokenKind.NeuronKw or TokenKind.Neuron => PortKind.Neuron,
            _ => PortKind.Neuron
        };
        if (Is(TokenKind.Synapse) || Is(TokenKind.Neuron)) Advance();
        else diagnostics.Error("INO213", "Expected synapse(...)/neuron(...).", Cur.Span);
        Expect(TokenKind.LParen, "INO214", "'('");
        var target = (IsIdent(Cur.Kind) || Is(TokenKind.Fqn)) ? Advance().Text : Expect(TokenKind.Fqn, "INO215", "a target FQN").Text;
        string? key = null;
        if (Is(TokenKind.LBracket))
        {
            Advance();
            key = Expect(TokenKind.String, "INO216", "a quoted resource key").Text;
            Expect(TokenKind.RBracket, "INO217", "']'");
        }
        Expect(TokenKind.RParen, "INO218", "')'");
        Expect(TokenKind.NewLine, "INO202", "end of line");
        var sigil = kind switch
        {
            PortKind.Synapse => PortSigil.Synapse,
            PortKind.Neuron => key is not null ? PortSigil.Resource : PortSigil.Call,
            _ => PortSigil.Call
        };
        return new UsingDecl(sigil, name, kind, target, key, kw.Span);
    }

    Handler ParseHandler()
    {
        var on = Advance();
        Trigger trigger;
        if (Is(TokenKind.Synapse))
        {
            // v5 C2: `on synapse(Fqn):` subscribes to a broadcast contract by FQN.
            // The legacy v4 alternative — `on synapse(localPort):` aliasing a
            // PortTrigger — never fired in any shipped scenario, so the parser
            // commits to the broadcast reading and unifies the two forms.
            Advance();
            Expect(TokenKind.LParen, "INO221", "'('");
            var f = (IsIdent(Cur.Kind) || Is(TokenKind.Fqn)) ? Advance().Text : Expect(TokenKind.Fqn, "INO222", "a trigger FQN").Text;
            Expect(TokenKind.RParen, "INO223", "')'");
            trigger = new BroadcastTrigger(f, on.Span);
        }
        else if (Is(TokenKind.Activated) || Is(TokenKind.Deactivated) || Is(TokenKind.Created))
        {
            var t = Advance();
            trigger = new LifecycleTrigger(t.Text, on.Span);
        }
        else if (Is(TokenKind.Failure))
        {
            Advance();
            var branch = ExpectIdent("INO229", "a speculative branch name").Text;
            trigger = new FailureTrigger(branch, on.Span);
        }
        else if (IsIdent(Cur.Kind) || Is(TokenKind.Fqn))
        {
            var name = Advance().Text;
            trigger = new PortTrigger(name, on.Span);
        }
        else
        {
            var t = Advance();
            trigger = new LifecycleTrigger(t.Text, on.Span);
        }

        Predicate? where = null;
        if (Is(TokenKind.Where))
        {
            Advance();
            var call = ParseCall();
            Expect(TokenKind.Is, "INO224", "'is'");
            var expected = Expect(TokenKind.String, "INO225", "a quoted expected value").Text;
            where = new Predicate(call, expected, call.Span);
        }
        Expect(TokenKind.Colon, "INO226", "':'");
        Expect(TokenKind.NewLine, "INO202", "end of line");
        Expect(TokenKind.Indent, "INO227", "an indented handler body");

        var body = new List<Stmt>();
        while (!Is(TokenKind.Dedent) && !Is(TokenKind.Eof)) body.Add(ParseStmt());
        Expect(TokenKind.Dedent, "INO228", "end of handler body");
        return new Handler(trigger, where, body, on.Span);
    }

    Stmt ParseStmt()
    {
        var t = Cur;
        switch (t.Kind)
        {
            case TokenKind.Speculate:
            {
                Advance();
                var branch = ExpectIdent("INO242", "a speculative branch name").Text;
                Expect(TokenKind.Colon, "INO226", "':'");
                Expect(TokenKind.NewLine, "INO202", "end of line");
                Expect(TokenKind.Indent, "INO227", "an indented speculation block");
                var body = new List<Stmt>();
                while (!Is(TokenKind.Dedent) && !Is(TokenKind.Eof)) body.Add(ParseStmt());
                Expect(TokenKind.Dedent, "INO228", "end of speculation body");
                return new SpeculateStmt(branch, body, t.Span);
            }
            case TokenKind.Verify:
            {
                Advance();
                var cond = ParseExpr();
                Expect(TokenKind.NewLine, "INO202", "end of line");
                return new VerifyStmt(cond, t.Span);
            }

            case TokenKind.Commit:
            {
                Advance();
                var branch = ExpectIdent("INO244", "a branch name to commit").Text;
                Expect(TokenKind.NewLine, "INO202", "end of line");
                return new CommitStmt(branch, t.Span);
            }
            case TokenKind.Rollback:
            {
                Advance();
                var branch = ExpectIdent("INO245", "a branch name to rollback").Text;
                Expect(TokenKind.NewLine, "INO202", "end of line");
                return new RollbackStmt(branch, t.Span);
            }
            case TokenKind.Let:
            {
                Advance();
                var v = ExpectIdent("INO230", "a variable name").Text;
                Expect(TokenKind.Equals, "INO231", "'='");
                if (Is(TokenKind.Ask))
                {
                    Advance();
                    var port = ExpectIdent("INO234", "a neuron port name").Text;
                    if (Is(TokenKind.To) || Is(TokenKind.For)) Advance();
                    var prompt = ParseExpr();
                    Expect(TokenKind.NewLine, "INO202", "end of line");
                    return new LetAskStmt(v, port, prompt, t.Span);
                }
                else
                {
                    var val = ParseExpr();
                    Expect(TokenKind.NewLine, "INO202", "end of line");
                    if (val is CallExpr c && c.Builtin.Contains('.'))
                    {
                        var lastDot = c.Builtin.LastIndexOf('.');
                        var port = c.Builtin[..lastDot];
                        var method = c.Builtin[(lastDot + 1)..];
                        var prompt = new CallExpr(method, c.Arg, c.Span);
                        return new LetAskStmt(v, port, prompt, t.Span);
                    }
                    return new LetExprStmt(v, val, t.Span);
                }
            }
            case TokenKind.Ask:
            {
                Advance();
                var port = (Is(TokenKind.Fqn) || IsIdent(Cur.Kind)) ? Advance().Text : ExpectIdent("INO234", "a neuron port name").Text;
                if (Is(TokenKind.To) || Is(TokenKind.For)) Advance();
                var prompt = ParseExpr();
                Expect(TokenKind.NewLine, "INO202", "end of line");
                return new LetAskStmt("_", port, prompt, t.Span);
            }
            case TokenKind.Emit:
            {
                Advance();
                var port = (Is(TokenKind.Fqn) || IsIdent(Cur.Kind)) ? Advance().Text : ExpectIdent("INO236", "a signal port name").Text;
                var args = ParseArgs();
                Expect(TokenKind.NewLine, "INO202", "end of line");
                return new EmitStmt(port, args, t.Span);
            }
            case TokenKind.Save:
            {
                Advance(); var val = ParseExpr();
                Expect(TokenKind.Into, "INO237", "'into'");
                var port = (Is(TokenKind.Fqn) || IsIdent(Cur.Kind)) ? Advance().Text : ExpectIdent("INO239", "a resource port name").Text;
                Expect(TokenKind.NewLine, "INO202", "end of line");
                return new SaveStmt(val, port, t.Span);
            }

            case TokenKind.Remember:
            {
                Advance();
                var text = ParseExpr();
                Expr? val = null;
                if (IsExprStart())
                {
                    val = ParseExpr();
                }
                Expect(TokenKind.NewLine, "INO202", "end of line");
                return new RememberStmt(text, val, t.Span);
            }
            case TokenKind.Count:
            {
                Advance(); var c = ExpectIdent("INO240", "a counter name").Text;
                Expect(TokenKind.NewLine, "INO202", "end of line");
                return new CountStmt(c, t.Span);
            }
            case TokenKind.Log:
            {
                Advance(); var m = ParseExpr();
                Expect(TokenKind.NewLine, "INO202", "end of line");
                return new LogStmt(m, t.Span);
            }
            case TokenKind.If:
            {
                Advance();
                var cond = ParseExpr();
                Expect(TokenKind.Colon, "INO226", "':'");
                Expect(TokenKind.NewLine, "INO202", "end of line");
                Expect(TokenKind.Indent, "INO227", "an indented then block");
                var thenBody = new List<Stmt>();
                while (!Is(TokenKind.Dedent) && !Is(TokenKind.Eof)) thenBody.Add(ParseStmt());
                Expect(TokenKind.Dedent, "INO228", "end of then block");
                
                var elseBody = new List<Stmt>();
                if (Is(TokenKind.Else))
                {
                    Advance();
                    Expect(TokenKind.Colon, "INO226", "':'");
                    Expect(TokenKind.NewLine, "INO202", "end of line");
                    Expect(TokenKind.Indent, "INO227", "an indented else block");
                    while (!Is(TokenKind.Dedent) && !Is(TokenKind.Eof)) elseBody.Add(ParseStmt());
                    Expect(TokenKind.Dedent, "INO228", "end of else block");
                }
                return new IfStmt(cond, thenBody, elseBody, t.Span);
            }
            case TokenKind.ForEach:
            {
                Advance();
                var varName = ExpectIdent("INO230", "a loop variable name").Text;
                Expect(TokenKind.In, "INO231", "'in'");
                var sourceList = ParseExpr();
                Expect(TokenKind.Colon, "INO226", "':'");
                Expect(TokenKind.NewLine, "INO202", "end of line");
                Expect(TokenKind.Indent, "INO227", "an indented loop body");
                var body = new List<Stmt>();
                while (!Is(TokenKind.Dedent) && !Is(TokenKind.Eof)) body.Add(ParseStmt());
                Expect(TokenKind.Dedent, "INO228", "end of loop body");
                return new ForEachStmt(varName, sourceList, body, t.Span);
            }
            case TokenKind.Write:
            {
                Advance();
                var target = ParseExpr();
                Expect(TokenKind.Equals, "INO231", "'='");
                var value = ParseExpr();
                Expect(TokenKind.NewLine, "INO202", "end of line");
                return new WriteStmt(target, value, t.Span);
            }
            default:
                if (IsIdent(Cur.Kind) || Is(TokenKind.Fqn))
                {
                    var source = ParseExpr();
                    if (Is(TokenKind.Arrow))
                    {
                        Advance(); // consume '->'
                        var target = ParseExpr();
                        Expect(TokenKind.NewLine, "INO202", "end of line");
                        return new FlowMappingStmt(source, target, t.Span);
                    }
                    else if (source is CallExpr c && c.Builtin.Contains('.'))
                    {
                        Expect(TokenKind.NewLine, "INO202", "end of line");
                        var lastDot = c.Builtin.LastIndexOf('.');
                        var port = c.Builtin[..lastDot];
                        var method = c.Builtin[(lastDot + 1)..];
                        var prompt = new CallExpr(method, c.Arg, c.Span);
                        return new LetAskStmt("_", port, prompt, t.Span);
                    }
                    else
                    {
                        diagnostics.Error("INO241", $"Expected '->' for flow mapping but found '{Cur.Text}'.", Cur.Span);
                        SkipToNextLine();
                        return new LogStmt(new StringExpr("", t.Span), t.Span);
                    }
                }
                diagnostics.Error("INO241", $"Unknown statement '{t.Text}'.", t.Span);
                while (!Is(TokenKind.NewLine) && !Is(TokenKind.Eof)) Advance();
                if (Is(TokenKind.NewLine)) Advance();
                return new LogStmt(new StringExpr("", t.Span), t.Span);
        }
    }

    bool IsExprStart() =>
        Is(TokenKind.String) || Is(TokenKind.Number) || IsIdent(Cur.Kind) ||
        Is(TokenKind.Fqn) || Is(TokenKind.It);

    NamedArg ParseOneArg()
    {
        var n = ExpectIdent("INO251", "an argument name").Text;
        Expect(TokenKind.Colon, "INO252", "colon");
        return new NamedArg(n, ParseExpr());
    }

    List<NamedArg> ParseArgs()
    {
        var args = new List<NamedArg>();
        Expect(TokenKind.LParen, "INO250", "'('");
        if (!Is(TokenKind.RParen))
        {
            args.Add(ParseOneArg());
            while (Is(TokenKind.Comma)) { Advance(); args.Add(ParseOneArg()); }
        }
        Expect(TokenKind.RParen, "INO253", "')'");
        return args;
    }

    CallExpr ParseCall()
    {
        var id = ExpectIdent("INO260", "a builtin name").Text;
        Expect(TokenKind.LParen, "INO261", "'('");
        var arg = ParseExpr();
        Expect(TokenKind.RParen, "INO262", "')'");
        return new CallExpr(id, arg, Cur.Span);
    }

    Expr ParseExpr()
    {
        var t = Cur;
        if (Is(TokenKind.String))
        {
            Advance();
            return t.Text.Contains('{') ? BuildInterp(t.Text, t.Span)
                                        : new StringExpr(t.Text, t.Span);
        }
        if (Is(TokenKind.Number))
        {
            Advance();
            if (!long.TryParse(t.Text, out var v))
            {
                // NumberExpr is integral; fractional numbers are only meaningful inside a
                // ui: data literal (which captures raw text and never reaches here).
                diagnostics.Error("INO232", $"'{t.Text}' is not a whole number; fractional numbers are only allowed in ui: data literals.", t.Span);
            }
            return new NumberExpr(v, t.Span);
        }

        if (Is(TokenKind.It))
        {
            Advance();
            if (Is(TokenKind.Dot))
            {
                Advance();
                var f = ExpectIdent("INO264", "a field name").Text;
                return new FieldAccessExpr("it", f, t.Span);
            }
            return new PortRefExpr("it", t.Span);
        }
        if (IsIdent(Cur.Kind) || Is(TokenKind.Fqn))
        {
            var name = Advance().Text;
            if (Is(TokenKind.LParen))
            {
                var lparenSpan = Cur.Span;
                Expect(TokenKind.LParen, "INO261", "'('");
                if (IsIdent(Cur.Kind) && Next.Kind == TokenKind.Colon)
                {
                    var args = new List<NamedArg>();
                    if (!Is(TokenKind.RParen))
                    {
                        args.Add(ParseOneArg());
                        while (Is(TokenKind.Comma)) { Advance(); args.Add(ParseOneArg()); }
                    }
                    Expect(TokenKind.RParen, "INO262", "')'");
                    return new CallExpr(name, new ArgsExpr(args, lparenSpan), t.Span);
                }
                else
                {
                    var arg = ParseExpr();
                    Expect(TokenKind.RParen, "INO262", "')'");
                    return new CallExpr(name, arg, t.Span);
                }
            }
            if (Is(TokenKind.Dot))
            {
                Advance();
                var f = ExpectIdent("INO264", "a field name").Text;
                return new FieldAccessExpr(name, f, t.Span);
            }
            if (name.Contains('.'))
            {
                var idx = name.LastIndexOf('.');
                return new FieldAccessExpr(name[..idx], name[(idx + 1)..], t.Span);
            }
            return new PortRefExpr(name, t.Span);
        }
        diagnostics.Error("INO265", $"Expected an expression but found '{t.Text}'.", t.Span);
        Advance();
        return new StringExpr("", t.Span);
    }

    Expr BuildInterp(string raw, SourceSpan span)
    {
        var parts = new List<Expr>();
        var i = 0;
        while (i < raw.Length)
        {
            var open = raw.IndexOf('{', i);
            if (open < 0) { parts.Add(new StringExpr(raw[i..], span)); break; }
            if (open > i) parts.Add(new StringExpr(raw[i..open], span));
            var close = raw.IndexOf('}', open);
            if (close < 0) { parts.Add(new StringExpr(raw[open..], span)); break; }
            var inner = raw[(open + 1)..close].Trim();
            parts.Add(ParseInnerRef(inner, span));
            i = close + 1;
        }
        return new InterpExpr(parts, span);
    }

    static Expr ParseInnerRef(string inner, SourceSpan span)
    {
        if (inner.Length > 0 && inner[0] is '#' or '!' or '$' or '~')
        {
            inner = inner[1..];
        }
        var dot = inner.IndexOf('.');
        return dot < 0
            ? new PortRefExpr(inner, span)
            : new FieldAccessExpr(inner[..dot], inner[(dot + 1)..], span);
    }

    ScenarioDecl ParseScenario()
    {
        var kw = Advance();
        var name = Expect(TokenKind.String, "INO270", "a quoted scenario name").Text;
        if (Is(TokenKind.Colon)) Advance();
        Expect(TokenKind.NewLine, "INO202", "end of line");
        Expect(TokenKind.Indent, "INO271", "an indented scenario body");
        var steps = new List<ScenarioStep>();
        while (!Is(TokenKind.Dedent) && !Is(TokenKind.Eof)) steps.Add(ParseScenarioStep());
        Expect(TokenKind.Dedent, "INO272", "end of scenario body");
        return new ScenarioDecl(name, steps, kw.Span);
    }

    // 'and' may continue an injection OR an assertion. Both spell `synapse`
    // since v5 C2 — distinguish by what follows the port name:
    //   synapse port(args)   → injection (next-after-port is '(')
    //   synapse port emitted → assertion (next-after-port is 'emitted')
    // Peek(2) is the token after the port name in `synapse port ...`.
    bool LooksLikeInjection() => Is(TokenKind.Synapse) && Peek(2).Kind == TokenKind.LParen;

    ScenarioStep ParseScenarioStep()
    {
        var t = Cur;
        if (Is(TokenKind.Given) || Is(TokenKind.Mock))
        {
            Advance();
            int idx = 0;
            bool isPredicate = false;
            while (true)
            {
                var kind = Peek(idx).Kind;
                if (kind == TokenKind.NewLine || kind == TokenKind.Eof) break;
                if (kind == TokenKind.Is) { isPredicate = true; break; }
                if (kind == TokenKind.Returns || kind == TokenKind.To) break;
                idx++;
            }

            if (isPredicate)
            {
                var call = ParseCall();
                Expect(TokenKind.Is, "INO281", "'is'");
                var ex = Expect(TokenKind.String, "INO282", "a quoted value").Text;
                Expect(TokenKind.NewLine, "INO202", "end of line");
                return new GivenPredicate(call, ex, t.Span);
            }
            else
            {
                var targetExpr = ParseExpr();
                string portName = targetExpr switch
                {
                    PortRefExpr p => p.Name,
                    FieldAccessExpr f => $"{f.PortName}.{f.Field}",
                    CallExpr c => c.Builtin,
                    _ => targetExpr.ToString()
                };
                if (Is(TokenKind.Returns) || Is(TokenKind.To)) Advance();
                var v = ParseExpr();
                Expect(TokenKind.NewLine, "INO202", "end of line");
                return new GivenNeuronReturns(portName, v, t.Span);
            }
        }

        Advance(); // consume 'when' | 'then' | 'and' | 'expect'

        if (t.Kind == TokenKind.Expect)
        {
            var expr = ParseExpr();
            Expect(TokenKind.NewLine, "INO202", "end of line");
            if (expr is CallExpr c)
            {
                string? fieldName = null;
                Expr? fieldValue = null;
                if (c.Arg is ArgsExpr args && args.Args.Count > 0)
                {
                    fieldName = args.Args[0].Name;
                    fieldValue = args.Args[0].Value;
                }
                return new ThenSynapseEmitted(c.Builtin, fieldName, fieldValue, t.Span);
            }
            return new ThenSynapseEmitted(expr.ToString(), null, null, t.Span);
        }

        if (t.Kind == TokenKind.When ||
            (t.Kind == TokenKind.And && LooksLikeInjection()))
        {
            if (Is(TokenKind.Synapse)) Advance();
            var port = (Is(TokenKind.Fqn) || IsIdent(Cur.Kind)) ? Advance().Text : ExpectIdent("INO284", "a port name").Text;
            var args = ParseArgs();
            Expect(TokenKind.NewLine, "INO202", "end of line");
            return new WhenInject(port, args, t.Span);
        }

        if (Is(TokenKind.Synapse))
        {
            Advance();
            var port = (Is(TokenKind.Fqn) || IsIdent(Cur.Kind)) ? Advance().Text : ExpectIdent("INO286", "a synapse port name").Text;
            Expect(TokenKind.Emitted, "INO287", "'emitted'");
            string? field = null; Expr? val = null;
            if (Is(TokenKind.With))
            {
                Advance();
                field = ExpectIdent("INO288", "a field name").Text;
                Expect(TokenKind.EqEq, "INO289", "'=='");
                val = ParseExpr();
            }
            Expect(TokenKind.NewLine, "INO202", "end of line");
            return new ThenSynapseEmitted(port, field, val, t.Span);
        }
        if (Is(TokenKind.Counter))
        {
            Advance();
            var c = ExpectIdent("INO290", "a counter name").Text;
            Expect(TokenKind.EqEq, "INO291", "'=='");
            var numTok = Expect(TokenKind.Number, "INO292", "a number");
            var n = long.TryParse(numTok.Text, out var parsed) ? parsed : 0L;
            Expect(TokenKind.NewLine, "INO202", "end of line");
            return new ThenCounter(c, n, t.Span);
        }
        var rport = ExpectIdent("INO294", "a resource port name").Text;
        Expect(TokenKind.Has, "INO295", "'has'");
        var hv = ParseExpr();
        Expect(TokenKind.NewLine, "INO202", "end of line");
        return new ThenResourceHas(rport, hv, t.Span);
    }



    UiDecl ParseUi()
    {
        var kw = Advance(); // consume 'ui'
        string cardName = "DefaultCard";
        if (IsIdent(Cur.Kind) || Is(TokenKind.Fqn))
        {
            cardName = Advance().Text;
        }
        Expect(TokenKind.Colon, "INO296", "':' after 'ui'");
        Expect(TokenKind.NewLine, "INO202", "end of line");
        Expect(TokenKind.Indent, "INO297", "an indented ui block");
        
        UiWidgetExpr? root = null;
        if (!Is(TokenKind.Dedent) && !Is(TokenKind.Eof))
        {
            root = ParseUiWidgetExpr();
        }
        
        Expect(TokenKind.Dedent, "INO301", "end of ui block");
        return new UiDecl(cardName, root, kw.Span);
    }

    void SkipUiLayoutWhitespaces()
    {
        while (Is(TokenKind.NewLine) || Is(TokenKind.Indent) || Is(TokenKind.Dedent))
        {
            Advance();
        }
    }

    UiWidgetExpr ParseUiWidgetExpr()
    {
        var start = Cur.Span;
        string widgetName = "";
        if (Is(TokenKind.Fqn) || IsIdent(Cur.Kind))
        {
            widgetName = Advance().Text;
            if (widgetName.StartsWith("UiKit."))
            {
                widgetName = widgetName["UiKit.".Length..];
            }
        }
        else
        {
            diagnostics.Error("INO302", "Expected widget name or UiKit constructor.", Cur.Span);
            Advance();
        }

        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rawJsonArgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var children = new List<UiWidgetExpr>();

        if (Is(TokenKind.LParen))
        {
            Advance(); // consume '('
            SkipUiLayoutWhitespaces();
            while (!Is(TokenKind.RParen) && !Is(TokenKind.Eof))
            {
                SkipUiLayoutWhitespaces();
                if (IsIdent(Cur.Kind) && (Next.Kind == TokenKind.Colon || Next.Kind == TokenKind.Equals))
                {
                    var argName = Advance().Text;
                    Advance(); // consume ':' or '='
                    SkipUiLayoutWhitespaces();
                    
                    if (Is(TokenKind.LBracket))
                    {
                        Advance(); // consume '['
                        SkipUiLayoutWhitespaces();
                        if (LooksLikeWidget())
                        {
                            while (!Is(TokenKind.RBracket) && !Is(TokenKind.Eof))
                            {
                                SkipUiLayoutWhitespaces();
                                if (LooksLikeWidget())
                                {
                                    children.Add(ParseUiWidgetExpr());
                                }
                                else
                                {
                                    Advance();
                                }
                                SkipUiLayoutWhitespaces();
                                if (Is(TokenKind.Comma)) Advance();
                            }
                            Expect(TokenKind.RBracket, "INO304", "']'");
                        }
                        else
                        {
                            // Data literal — e.g. points: [{lat,lng},…] / arcs: [{from,to,style},…].
                            // Capture as a JSON array string so EarthGlobe coordinates survive
                            // UiLayoutJson → the client bridge as real arrays, not quoted scalars.
                            arguments[argName] = CaptureJsonArrayBody();
                            rawJsonArgs.Add(argName);
                        }
                    }
                    else if ((Is(TokenKind.Fqn) || IsIdent(Cur.Kind)) && (Cur.Text.StartsWith("UiKit.") || Cur.Text == "UiKit" || Cur.Text.Contains('.')))
                    {
                        children.Add(ParseUiWidgetExpr());
                    }
                    else
                    {
                        var argVal = StringifyExpr(ParseExpr());
                        arguments[argName] = argVal;
                    }
                }
                else
                {
                    if ((Is(TokenKind.Fqn) || IsIdent(Cur.Kind)) && (Cur.Text.StartsWith("UiKit.") || Cur.Text == "UiKit" || Cur.Text.Contains('.')))
                    {
                        children.Add(ParseUiWidgetExpr());
                    }
                    else
                    {
                        var argVal = StringifyExpr(ParseExpr());
                        arguments[$"arg{arguments.Count}"] = argVal;
                    }
                }
                
                SkipUiLayoutWhitespaces();
                if (Is(TokenKind.Comma)) Advance();
            }
            Expect(TokenKind.RParen, "INO303", "')'");
        }

        bool isContainer = false;
        if (Is(TokenKind.Colon))
        {
            Advance(); // consume ':'
            isContainer = true;
        }
        else if (Is(TokenKind.NewLine) && Next.Kind == TokenKind.Indent)
        {
            isContainer = true;
        }

        if (isContainer)
        {
            Expect(TokenKind.NewLine, "INO202", "end of line");
            Expect(TokenKind.Indent, "INO305", "an indented children block");
            while (!Is(TokenKind.Dedent) && !Is(TokenKind.Eof))
            {
                children.Add(ParseUiWidgetExpr());
                SkipNewLines();
            }
            Expect(TokenKind.Dedent, "INO306", "end of children block");
        }
        else
        {
            SkipNewLines();
        }
        
        return new UiWidgetExpr(widgetName, arguments, children, new SourceSpan(start.Start, Cur.Span.End))
        {
            RawJsonArgs = rawJsonArgs,
        };
    }

    bool LooksLikeWidget() =>
        (Is(TokenKind.Fqn) || IsIdent(Cur.Kind)) &&
        (Cur.Text.StartsWith("UiKit.") || Cur.Text == "UiKit" || Cur.Text.Contains('.'));

    // Reconstructs a ui: data literal ([{lat,lng},…] / nested maps / scalars) into a
    // JSON string. Used for widget args that carry structured data (EarthGlobe points/arcs)
    // rather than child widgets. The '[' is assumed already consumed by the caller.
    string CaptureJsonArrayBody()
    {
        var sb = new System.Text.StringBuilder("[");
        SkipUiLayoutWhitespaces();
        bool first = true;
        while (!Is(TokenKind.RBracket) && !Is(TokenKind.Eof))
        {
            SkipUiLayoutWhitespaces();
            if (Is(TokenKind.RBracket)) break;
            if (!first) sb.Append(',');
            first = false;
            sb.Append(CaptureJsonValue());
            SkipUiLayoutWhitespaces();
            if (Is(TokenKind.Comma)) { Advance(); SkipUiLayoutWhitespaces(); }
        }
        Expect(TokenKind.RBracket, "INO304", "']'");
        sb.Append(']');
        return sb.ToString();
    }

    string CaptureJsonObjectBody()
    {
        var sb = new System.Text.StringBuilder("{");
        SkipUiLayoutWhitespaces();
        bool first = true;
        while (!Is(TokenKind.RBrace) && !Is(TokenKind.Eof))
        {
            SkipUiLayoutWhitespaces();
            if (Is(TokenKind.RBrace)) break;
            if (!Is(TokenKind.String) && !Is(TokenKind.Fqn) && !IsIdent(Cur.Kind))
            {
                diagnostics.Error("INO311", "Expected a field name in a ui: data literal map.", Cur.Span);
            }
            var key = Advance().Text;
            Expect(TokenKind.Colon, "INO307", "':' in a map literal");
            SkipUiLayoutWhitespaces();
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(JsonEscape(key)).Append("\":").Append(CaptureJsonValue());
            SkipUiLayoutWhitespaces();
            if (Is(TokenKind.Comma)) { Advance(); SkipUiLayoutWhitespaces(); }
        }
        Expect(TokenKind.RBrace, "INO308", "'}'");
        sb.Append('}');
        return sb.ToString();
    }

    string CaptureJsonValue()
    {
        if (Is(TokenKind.LBracket)) { Advance(); return CaptureJsonArrayBody(); }
        if (Is(TokenKind.LBrace)) { Advance(); return CaptureJsonObjectBody(); }
        if (Is(TokenKind.Number)) return Advance().Text;
        if (Is(TokenKind.String)) return "\"" + JsonEscape(Advance().Text) + "\"";
        if (Is(TokenKind.Fqn) || IsIdent(Cur.Kind))
        {
            var t = Advance().Text;
            return t is "true" or "false" or "null" ? t : "\"" + JsonEscape(t) + "\"";
        }
        diagnostics.Error("INO309", "Unexpected token in a ui: data literal.", Cur.Span);
        Advance();
        return "null";
    }

    static string JsonEscape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private string StringifyExpr(Expr expr)
    {
        return expr switch
        {
            StringExpr s => s.Value,
            NumberExpr n => n.Value.ToString(),
            PortRefExpr p => p.Name,
            FieldAccessExpr f => $"{f.PortName}.{f.Field}",
            CallExpr c => $"{c.Builtin}({StringifyExpr(c.Arg)})",
            InterpExpr i => string.Join("", i.Parts.Select(StringifyExpr)),
            _ => expr.ToString() ?? ""
        };
    }

    void SkipToNextLine()
    {
        while (!Is(TokenKind.NewLine) && !Is(TokenKind.Eof)) Advance();
        if (Is(TokenKind.NewLine)) Advance();
    }
}

