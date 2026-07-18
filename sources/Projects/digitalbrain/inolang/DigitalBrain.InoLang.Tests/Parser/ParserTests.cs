using DigitalBrain.InoLang.Ast;
using DigitalBrain.InoLang.Diagnostics;
using DigitalBrain.InoLang.Lexing;
using DigitalBrain.InoLang.Parsing;

namespace DigitalBrain.InoLang.Tests.Parsing;

public sealed class ParserTests
{
    static NeuronDoc Parse(string s)
    {
        var bag = new DiagnosticBag();
        var toks = new Lexer(s, bag).Lex();
        var doc = new Parser(toks, bag).ParseDocument();
        bag.HasErrors.Should().BeFalse(string.Join(";", bag.Items.Select(i => i.Message)));
        return doc!;
    }

    const string Doc = """
        neuron Acme.BusinessAnalyst
          "Becomes a domain-expert business analyst."
          using ask   = synapse(DigitalBrain.User.Request)
          using gpt   = neuron(DigitalBrain.SDK.ChatGpt)
          using db    = neuron(DigitalBrain.Data.Sqlite["analysis"])
          using ready = signal(Acme.AnalysisReady)
          @telemetry:counter:analyses_completed
          on activated:
            log "spawned"
          on ask where topic-of(ask.text) is "Car Insurance":
            let summary = ask gpt to "analyze {ask.text}"
            save summary into db
            count analyses_completed
            emit ready(summary: summary)

        scenario "produces an analysis"
          given topic-of(ask.text) is "Car Insurance"
          given gpt returns "crowded market"
          when synapse ask(text: "car insurance startup")
          then db has "crowded market"
          and signal ready emitted with summary == "crowded market"
          and counter analyses_completed == 1
        """;

    [Fact]
    public void Parses_full_north_star_document()
    {
        var d = Parse(Doc);
        d.Fqn.Should().Be("Acme.BusinessAnalyst");
        d.Intent.Should().Be("Becomes a domain-expert business analyst.");
        d.Usings.Select(u => u.Name).Should().Equal("ask", "gpt", "db", "ready");
        d.Usings.Single(u => u.Name == "db").Key.Should().Be("analysis");
        d.Counters.Should().Equal("analyses_completed");
        d.Handlers.Should().HaveCount(2);
        d.Handlers[1].Where!.Expected.Should().Be("Car Insurance");
        d.Handlers[1].Body.OfType<EmitStmt>().Single().Port.Should().Be("ready");
        d.Scenarios.Should().ContainSingle();
        d.Scenarios[0].Steps.OfType<ThenCounter>().Single().Value.Should().Be(1);
    }

    [Fact]
    public void Missing_neuron_keyword_reports_error()
    {
        var bag = new DiagnosticBag();
        var toks = new Lexer("using x = synapse(A.B)\n", bag).Lex();
        new Parser(toks, bag).ParseDocument();
        bag.HasErrors.Should().BeTrue();
        bag.Items.Should().Contain(d => d.Code == "INO200");
    }

    [Fact]
    public void Bad_counter_number_is_a_diagnostic_not_an_exception()
    {
        const string src = """
            neuron A.X
              using ask   = synapse(A.Req)
              using ready = signal(A.Done)
              on ask:
                emit ready(ok: "1")
            scenario "s"
              when synapse ask(text: "t")
              and counter c == notanumber
            """;
        var bag = new DiagnosticBag();
        var toks = new Lexer(src, bag).Lex();
        Action act = () => new Parser(toks, bag).ParseDocument();
        act.Should().NotThrow();
        bag.Items.Should().Contain(d => d.Code == "INO292");
    }

    [Fact]
    public void Parses_speculation_primitives()
    {
        const string src = """
            neuron Acme.TestAgent
              using ask   = synapse(DigitalBrain.User.Request)
              using ready = signal(Acme.Done)
              on ask:
                speculate TrialBranch:
                  let val = ask ready to "generate answer"
                  verify topic-is-insurance(val)
                  commit TrialBranch
                  emit ready(msg: val)
              on failure TrialBranch:
                rollback TrialBranch
                emit ready(msg: "failed speculation")
            """;

        var d = Parse(src);
        d.Fqn.Should().Be("Acme.TestAgent");
        d.Handlers.Should().HaveCount(2);

        // Handler 0 has the speculation body
        var handler0 = d.Handlers[0];
        handler0.Trigger.Should().BeOfType<PortTrigger>();
        var spec = handler0.Body.Should().ContainSingle().Subject.Should().BeOfType<SpeculateStmt>().Subject;
        spec.Branch.Should().Be("TrialBranch");
        spec.Body.Should().HaveCount(4);
        
        spec.Body[0].Should().BeOfType<LetAskStmt>();

        spec.Body[1].Should().BeOfType<VerifyStmt>();
        var verify = (VerifyStmt)spec.Body[1];
        verify.Cond.Should().BeOfType<CallExpr>();

        spec.Body[2].Should().BeOfType<CommitStmt>();
        ((CommitStmt)spec.Body[2]).Branch.Should().Be("TrialBranch");

        spec.Body[3].Should().BeOfType<EmitStmt>();

        // Handler 1 is the failure trigger
        var handler1 = d.Handlers[1];
        handler1.Trigger.Should().BeOfType<FailureTrigger>();
        ((FailureTrigger)handler1.Trigger).Branch.Should().Be("TrialBranch");
        handler1.Body[0].Should().BeOfType<RollbackStmt>();
        ((RollbackStmt)handler1.Body[0]).Branch.Should().Be("TrialBranch");
    }

    [Fact]
    public void Parses_method_style_named_arguments()
    {
        const string src = """
            neuron Inferred.Neuron
              "System-level OS manager neuron."
              on System.ExecuteCommand:
                let current = DigitalBrain.User.Current.Location
                let destination = ride.destination
                let trip = Uber.Taxi.Call(from: current, to: destination)
                log "Taxi dispatched! ETA: {trip.eta}"
            """;
        var d = Parse(src);
        d.Fqn.Should().Be("Inferred.Neuron");
        d.Intent.Should().Be("System-level OS manager neuron.");
        d.Usings.Should().BeEmpty();
        d.Handlers.Should().ContainSingle();
        
        var handler = d.Handlers[0];
        handler.Trigger.Should().BeOfType<PortTrigger>();
        ((PortTrigger)handler.Trigger).Port.Should().Be("System.ExecuteCommand");
        
        var body = handler.Body;
        body.Should().HaveCount(4);
        
        body[2].Should().BeOfType<LetAskStmt>();
        var letTrip = (LetAskStmt)body[2];
        letTrip.Var.Should().Be("trip");
        letTrip.Port.Should().Be("Uber.Taxi");
        letTrip.Prompt.Should().BeOfType<CallExpr>();
        
        var call = (CallExpr)letTrip.Prompt;
        call.Builtin.Should().Be("Call");
        call.Arg.Should().BeOfType<ArgsExpr>();
        
        var argsExpr = (ArgsExpr)call.Arg;
        argsExpr.Args.Should().HaveCount(2);
        argsExpr.Args[0].Name.Should().Be("from");
        argsExpr.Args[0].Value.Should().BeOfType<PortRefExpr>();
        ((PortRefExpr)argsExpr.Args[0].Value).Name.Should().Be("current");
        
        argsExpr.Args[1].Name.Should().Be("to");
        argsExpr.Args[1].Value.Should().BeOfType<PortRefExpr>();
        ((PortRefExpr)argsExpr.Args[1].Value).Name.Should().Be("destination");
    }



    [Fact]
    public void Parses_direct_dot_notation_calls()
    {
        const string src = """
            neuron Acme.TestDotNotation
              using word = neuron(Microsoft.Word)
              
              on Activated:
                let doc = word.NewDocument(title: "Digest")
                word.SaveAs(path: "C:/reports/digest.docx")
                
            scenario "happy path"
              when Activated()
              then counter test == 1
            """;

        var d = Parse(src);
        d.Handlers.Should().ContainSingle();
        var body = d.Handlers[0].Body;
        body.Should().HaveCount(2);

        var first = body[0].Should().BeOfType<LetAskStmt>().Subject;
        first.Var.Should().Be("doc");
        first.Port.Should().Be("word");
        var firstPrompt = first.Prompt.Should().BeOfType<CallExpr>().Subject;
        firstPrompt.Builtin.Should().Be("NewDocument");

        var second = body[1].Should().BeOfType<LetAskStmt>().Subject;
        second.Var.Should().Be("_");
        second.Port.Should().Be("word");
        var secondPrompt = second.Prompt.Should().BeOfType<CallExpr>().Subject;
        secondPrompt.Builtin.Should().Be("SaveAs");
    }
}

