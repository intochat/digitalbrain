using DigitalBrain.InoLang.Diagnostics;
using DigitalBrain.InoLang.Lexing;
using DigitalBrain.InoLang.Parsing;
using DigitalBrain.InoLang.Tests;

namespace DigitalBrain.InoLang.Tests.Parsing;

public sealed class InterpreterTests
{
    sealed class FakeNeurons : INeuronHost
    {
        public Dictionary<string, string> NeuronReturns { get; } = [];
        // Pinned target per builtin — true iff the runtime's target matches.
        // Mirrors StubNeuronHost's E-SDK #58 shape.
        public Dictionary<string, string> Predicates { get; } = [];
        // Records the (subject, target) the Interpreter forwarded so tests
        // can assert the runtime evaluated `#ask.text` (etc.) before calling
        // the neuron — without this, an Interpreter regression that passed the
        // literal AST node or an empty string as subject would silently pass.
        public List<(string Builtin, string Subject, string Target)> PredicateCalls { get; } = [];
        public List<(string Port, string Prompt)> AskCalls { get; } = [];

        public Task<string> AskAsync(string port, string prompt, CancellationToken ct)
        {
            AskCalls.Add((port, prompt));
            return Task.FromResult(NeuronReturns.GetValueOrDefault(port, ""));
        }
        public Task<bool> EvaluatePredicateAsync(string builtin, string subject, string target, CancellationToken ct)
        {
            PredicateCalls.Add((builtin, subject, target));
            return Task.FromResult(
                Predicates.TryGetValue(builtin, out var pinned)
                && string.Equals(pinned, target, StringComparison.Ordinal));
        }
    }

    static ExecutionPlan Plan(string src, out DiagnosticBag bag)
    {
        bag = new DiagnosticBag();
        var doc = new Parser(new Lexer(src, bag).Lex(), bag).ParseDocument();
        var cat = DeferredContractCatalog.Instance;
        var linked = new Linker(cat, bag).Link(doc!);
        bag.HasErrors.Should().BeFalse(string.Join(";", bag.Items.Select(i => i.Message)));
        return Lowering.Lower(linked!);
    }

    const string Src = """
        neuron A.X
          using ask   = synapse(A.Req)
          using gpt   = neuron(A.Gpt)
          using db    = neuron(A.Db)
          using ready = signal(A.Done)
          @telemetry:counter:done
          on ask where topic-of(ask.text) is "Car Insurance":
            let s = ask gpt to "analyze {ask.text}"
            save s into db
            count done
            emit ready(summary: s)
        scenario "x"
          when synapse ask(text: "t")
          then signal ready emitted
        """;

    [Fact]
    public async Task Predicate_true_runs_body_and_collects_effects()
    {
        var plan = Plan(Src, out _);
        var handler = plan.HandlersFor(TriggerKey.Port("ask"))[0];
        var w = handler.Where!;


        var neurons = new FakeNeurons();
        neurons.Predicates["topic-of"] = "Car Insurance";
        neurons.NeuronReturns["gpt"] = "crowded market";

        var r = await new Interpreter(plan).RunAsync(
            TriggerKey.Port("ask"),
            new Dictionary<string, string> { ["text"] = "car insurance biz" },
            neurons, CancellationToken.None);

        r.EmittedSynapses.Should().ContainSingle(e => e.Port == "ready");
        r.EmittedSynapses[0].Args["summary"].Should().Be("crowded market");
        r.SavedResources["db"].Should().Be("crowded market");
        r.Counters["done"].Should().Be(1);
        // E-SDK #58. The runtime must forward the *evaluated* subject
        // (ask.text → "car insurance biz") and the .ino's `is "..."`
        // literal as the target — a regression that passes the AST node
        // or an empty string would otherwise slip past the predicate-fires
        // assertions above (StubNeuronHost ignores subject in its pin check).
        neurons.PredicateCalls.Should().ContainSingle()
            .Which.Should().Be(("topic-of", "car insurance biz", "Car Insurance"));
    }

    [Fact]
    public async Task Predicate_false_skips_body()
    {
        var plan = Plan(Src, out _);
        var neurons = new FakeNeurons();
        neurons.Predicates["topic-of"] = "Something Else";

        var r = await new Interpreter(plan).RunAsync(
            TriggerKey.Port("ask"),
            new Dictionary<string, string> { ["text"] = "x" },
            neurons, CancellationToken.None);

        r.EmittedSynapses.Should().BeEmpty();
        r.Counters.Should().NotContainKey("done");
    }

    [Fact]
    public async Task Duplicate_emit_arg_does_not_throw_last_wins()
    {
        const string dup = """
            neuron A.X
              using ask   = synapse(A.Req)
              using ready = signal(A.Done)
              on ask:
                emit ready(summary: "a", summary: "b")
            scenario "x"
              when synapse ask(text: "t")
              then signal ready emitted
            """;
        var plan = Plan(dup, out _);
        var r = await new Interpreter(plan).RunAsync(
            TriggerKey.Port("ask"),
            new Dictionary<string, string> { ["text"] = "t" },
            new FakeNeurons(), CancellationToken.None);
        r.EmittedSynapses.Should().ContainSingle();
        r.EmittedSynapses[0].Args["summary"].Should().Be("b");
    }

    [Fact]
    public async Task Foreach_trims_items_and_skips_empty_values()
    {
        const string src = """
            neuron A.X
              using ask   = synapse(A.Req)
              using ready = signal(A.Done)
              on ask:
                foreach item in " alpha, ,beta , gamma ":
                  emit ready(summary: item)
            """;
        var plan = Plan(src, out _);
        var r = await new Interpreter(plan).RunAsync(
            TriggerKey.Port("ask"),
            new Dictionary<string, string> { ["text"] = "t" },
            new FakeNeurons(), CancellationToken.None);

        r.EmittedSynapses.Select(e => e.Args["summary"])
            .Should().Equal("alpha", "beta", "gamma");
    }

    [Fact]
    public async Task Speculation_success_commits_all_changes_cleanly()
    {
        const string src = """
            neuron A.X
              using ask   = synapse(A.Req)
              using ready = signal(A.Done)
              using db    = neuron(A.Db)
              @telemetry:counter:spec_success
              on ask:
                speculate TrialBranch:
                  let inner_var = "trial_success"
                  save inner_var into db
                  count spec_success
                  verify "true"
                  commit TrialBranch
                  emit ready(summary: inner_var)
            """;

        var plan = Plan(src, out _);
        var r = await new Interpreter(plan).RunAsync(
            TriggerKey.Port("ask"),
            new Dictionary<string, string> { ["text"] = "t" },
            new FakeNeurons(), CancellationToken.None);

        // Assert that the speculative changes were successfully committed to the parent context
        r.SavedResources["db"].Should().Be("trial_success");
        r.Counters["spec_success"].Should().Be(1);
        r.EmittedSynapses.Should().ContainSingle(e => e.Port == "ready");
        r.EmittedSynapses[0].Args["summary"].Should().Be("trial_success");
    }

    [Fact]
    public async Task Speculation_failure_rolls_back_and_routes_to_failure_handler()
    {
        const string src = """
            neuron A.X
              using ask   = synapse(A.Req)
              using ready = signal(A.Done)
              using db    = neuron(A.Db)
              @telemetry:counter:spec_success
              @telemetry:counter:fail_handler_runs
              on ask:
                speculate TrialBranch:
                  save "speculative_value" into db
                  count spec_success
                  verify "false"
                  commit TrialBranch
              on failure TrialBranch:
                rollback TrialBranch
                count fail_handler_runs
                emit ready(summary: "failure_fallback")
            """;

        var plan = Plan(src, out _);
        var r = await new Interpreter(plan).RunAsync(
            TriggerKey.Port("ask"),
            new Dictionary<string, string> { ["text"] = "t" },
            new FakeNeurons(), CancellationToken.None);

        // Assert that all speculative changes were completely discarded
        r.SavedResources.Should().NotContainKey("db");
        r.Counters.Should().NotContainKey("spec_success");
        
        // Assert that the failure handler successfully executed in the parent context
        r.Counters["fail_handler_runs"].Should().Be(1);
        r.EmittedSynapses.Should().ContainSingle(e => e.Port == "ready");
        r.EmittedSynapses[0].Args["summary"].Should().Be("failure_fallback");
    }

    [Fact]
    public async Task Explicit_rollback_statement_aborts_speculation_and_runs_failure_handler()
    {
        const string src = """
            neuron A.X
              using ask   = synapse(A.Req)
              using ready = signal(A.Done)
              using db    = neuron(A.Db)
              @telemetry:counter:spec_runs
              @telemetry:counter:fail_handler_runs
              on ask:
                speculate TrialBranch:
                  save "explicit_rollback" into db
                  count spec_runs
                  rollback TrialBranch
                  save "should_never_reach_here" into db
              on failure TrialBranch:
                count fail_handler_runs
                emit ready(summary: "rolled_back")
            """;

        var plan = Plan(src, out _);
        var r = await new Interpreter(plan).RunAsync(
            TriggerKey.Port("ask"),
            new Dictionary<string, string> { ["text"] = "t" },
            new FakeNeurons(), CancellationToken.None);

        // Assert rollback of state
        r.SavedResources.Should().NotContainKey("db");
        r.Counters.Should().NotContainKey("spec_runs");
        
        // Assert failure handler trigger
        r.Counters["fail_handler_runs"].Should().Be(1);
        r.EmittedSynapses.Should().ContainSingle(e => e.Port == "ready");
        r.EmittedSynapses[0].Args["summary"].Should().Be("rolled_back");
    }

    [Fact]
    public async Task Nested_speculation_handles_isolated_commits_and_rollbacks()
    {
        const string src = """
            neuron A.X
              using ask   = synapse(A.Req)
              using ready = signal(A.Done)
              using db    = neuron(A.Db)
              @telemetry:counter:outer_count
              @telemetry:counter:inner_count
              @telemetry:counter:inner_failed
              on ask:
                speculate OuterBranch:
                  save "outer_val" into db
                  count outer_count
                  
                  speculate InnerBranch:
                    save "inner_val" into db
                    count inner_count
                    verify "true"
                    commit InnerBranch
                  
                  commit OuterBranch
                  emit ready(summary: "nested_success")
            """;

        var plan = Plan(src, out _);
        var r = await new Interpreter(plan).RunAsync(
            TriggerKey.Port("ask"),
            new Dictionary<string, string> { ["text"] = "t" },
            new FakeNeurons(), CancellationToken.None);

        // Assert that inner committed successfully to outer, which committed successfully to root
        r.SavedResources["db"].Should().Be("inner_val"); // Inner overwrote Outer's save
        r.Counters["outer_count"].Should().Be(1);
        r.Counters["inner_count"].Should().Be(1);
        r.EmittedSynapses.Should().ContainSingle(e => e.Port == "ready");
        r.EmittedSynapses[0].Args["summary"].Should().Be("nested_success");
    }

    [Fact]
    public async Task Nested_inner_speculation_rollback_does_not_affect_outer_speculation()
    {
        const string src = """
            neuron A.X
              using ask   = synapse(A.Req)
              using ready = signal(A.Done)
              using db    = neuron(A.Db)
              @telemetry:counter:outer_count
              @telemetry:counter:inner_failed
              on ask:
                speculate OuterBranch:
                  save "outer_val" into db
                  count outer_count
                  
                  speculate InnerBranch:
                    save "inner_val" into db
                    verify "false"
                    commit InnerBranch
                  
                  commit OuterBranch
                  emit ready(summary: "outer_only")
              on failure InnerBranch:
                rollback InnerBranch
                count inner_failed
            """;

        var plan = Plan(src, out _);
        var r = await new Interpreter(plan).RunAsync(
            TriggerKey.Port("ask"),
            new Dictionary<string, string> { ["text"] = "t" },
            new FakeNeurons(), CancellationToken.None);

        // Assert that Inner rolled back, but Outer committed successfully!
        r.SavedResources["db"].Should().Be("outer_val");
        r.Counters["outer_count"].Should().Be(1);
        r.Counters["inner_failed"].Should().Be(1);
        r.EmittedSynapses.Should().ContainSingle(e => e.Port == "ready");
        r.EmittedSynapses[0].Args["summary"].Should().Be("outer_only");
    }

    [Fact]
    public async Task Interpreter_emits_telemetry_traces_for_all_speculative_primitives_and_asks()
    {
        const string src = """
            neuron A.X
              using ask   = synapse(A.Req)
              using gpt   = neuron(A.Gpt)
              using ready = signal(A.Done)
              on ask:
                speculate TraceBranch:
                  let q = ask gpt to "think speculative"
                  verify "true"
                  commit TraceBranch
                  emit ready(summary: q)
            """;

        var plan = Plan(src, out _);
        var traces = new List<(string Branch, string Action, string Desc, double Conf, string State, Guid StepId, Guid? ParentId)>();

        var interpreter = new Interpreter(plan)
        {
            OnTrace = (branch, action, desc, conf, state, stepId, parentId) =>
            {
                traces.Add((branch, action, desc, conf, state, stepId, parentId));
            }
        };

        var neurons = new FakeNeurons();
        neurons.NeuronReturns["gpt"] = "gpt_response";

        await interpreter.RunAsync(
            TriggerKey.Port("ask"),
            new Dictionary<string, string> { ["text"] = "test" },
            neurons, CancellationToken.None);

        // Verify traces
        traces.Should().HaveCount(4);

        // 1. SpeculateStart
        var specStart = traces[0];
        specStart.Branch.Should().Be("TraceBranch");
        specStart.Action.Should().Be("SpeculateStart");
        specStart.StepId.Should().NotBeEmpty();
        specStart.ParentId.Should().BeNull();

        // 2. AskCall
        var askCall = traces[1];
        askCall.Branch.Should().Be("TraceBranch");
        askCall.Action.Should().Be("AskCall");
        askCall.Desc.Should().Contain("think speculative");
        askCall.StepId.Should().NotBeEmpty();
        askCall.ParentId.Should().Be(specStart.StepId);

        // 3. VerifyPass
        var verifyPass = traces[2];
        verifyPass.Branch.Should().Be("TraceBranch");
        verifyPass.Action.Should().Be("VerifyPass");
        verifyPass.StepId.Should().NotBeEmpty();
        verifyPass.ParentId.Should().Be(specStart.StepId);

        // 4. Commit
        var commit = traces[3];
        commit.Branch.Should().Be("TraceBranch");
        commit.Action.Should().Be("Commit");
        commit.StepId.Should().NotBeEmpty();
        commit.ParentId.Should().Be(specStart.StepId);
    }

    [Fact]
    public async Task Interpreter_emits_verify_fail_and_rollback_telemetry_traces()
    {
        const string src = """
            neuron A.X
              using ask   = synapse(A.Req)
              on ask:
                speculate TraceBranch:
                  verify "false"
              on failure TraceBranch:
                rollback TraceBranch
            """;

        var plan = Plan(src, out _);
        var traces = new List<(string Branch, string Action, string Desc, double Conf, string State, Guid StepId, Guid? ParentId)>();

        var interpreter = new Interpreter(plan)
        {
            OnTrace = (branch, action, desc, conf, state, stepId, parentId) =>
            {
                traces.Add((branch, action, desc, conf, state, stepId, parentId));
            }
        };

        await interpreter.RunAsync(
            TriggerKey.Port("ask"),
            new Dictionary<string, string> { ["text"] = "test" },
            new FakeNeurons(), CancellationToken.None);

        // Verify traces
        traces.Should().HaveCount(4);

        // 1. SpeculateStart
        var specStart = traces[0];
        specStart.Branch.Should().Be("TraceBranch");
        specStart.Action.Should().Be("SpeculateStart");
        specStart.StepId.Should().NotBeEmpty();

        // 2. VerifyFail
        var verifyFail = traces[1];
        verifyFail.Branch.Should().Be("TraceBranch");
        verifyFail.Action.Should().Be("VerifyFail");
        verifyFail.StepId.Should().NotBeEmpty();
        verifyFail.ParentId.Should().Be(specStart.StepId);

        // 3. Rollback (from the catch block)
        var rollback1 = traces[2];
        rollback1.Branch.Should().Be("TraceBranch");
        rollback1.Action.Should().Be("Rollback");
        rollback1.StepId.Should().NotBeEmpty();
        rollback1.ParentId.Should().Be(specStart.StepId);

        // 4. Rollback (from the failure handler's rollback statement)
        var rollback2 = traces[3];
        rollback2.Branch.Should().Be("TraceBranch");
        rollback2.Action.Should().Be("Rollback");
        rollback2.StepId.Should().NotBeEmpty();
        rollback2.ParentId.Should().Be(specStart.StepId);
    }

    [Fact]
    public async Task Executes_direct_dot_notation_calls_correctly()
    {
        const string src = """
            neuron A.X
              using ask  = synapse(A.Req)
              using word = neuron(A.Gpt)
              on ask:
                let doc = word.NewDocument(title: "Digest")
                word.SaveAs(doc: doc, path: "C:/reports/digest.docx")
            """;

        var plan = Plan(src, out _);
        var neurons = new FakeNeurons();
        neurons.NeuronReturns["word"] = "digest_handle";

        await new Interpreter(plan).RunAsync(
            TriggerKey.Port("ask"),
            new Dictionary<string, string> { ["text"] = "t" },
            neurons, CancellationToken.None);

        neurons.AskCalls.Should().HaveCount(2);
        
        neurons.AskCalls[0].Port.Should().Be("word");
        neurons.AskCalls[0].Prompt.Should().Be("NewDocument title:Digest");

        neurons.AskCalls[1].Port.Should().Be("word");
        neurons.AskCalls[1].Prompt.Should().Be("SaveAs doc:digest_handle,path:C:/reports/digest.docx");
    }
}
