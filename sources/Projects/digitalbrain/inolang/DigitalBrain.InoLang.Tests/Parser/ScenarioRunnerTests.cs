using DigitalBrain.InoLang.Diagnostics;
using DigitalBrain.InoLang.Lexing;
using DigitalBrain.InoLang.Parsing;
using DigitalBrain.InoLang.Linking;
using DigitalBrain.InoLang.Tests;
using Xunit;
using FluentAssertions;

namespace DigitalBrain.InoLang.Tests.Parsing;

public sealed class ScenarioRunnerTests
{
    private sealed class FakeCatalog : IContractCatalog
    {
        private readonly Dictionary<string, ContractSchema> _schemas = new(StringComparer.Ordinal);

        public ContractSchema? Resolve(string fqn) => _schemas.GetValueOrDefault(fqn);

        public IReadOnlyCollection<ContractSchema> GetAllSchemas() => _schemas.Values;

        public void Register(ContractSchema schema) => _schemas[schema.Fqn] = schema;

        public FakeCatalog With(string fqn, ContractKind kind, params string[] fields)
        {
            _schemas[fqn] = new ContractSchema(fqn, kind, fields);
            return this;
        }
    }

    static ExecutionPlan Plan(string src)
    {
        var bag = new DiagnosticBag();
        var doc = new Parser(new Lexer(src, bag).Lex(), bag).ParseDocument();
        var cat = new FakeCatalog()
            .With("A.Req", ContractKind.Synapse, "text")
            .With("A.Done", ContractKind.Synapse, "summary")
            .With("A.Gpt", ContractKind.Neuron)
            .With("A.Db", ContractKind.Neuron);
        var linked = new Linker(cat, bag).Link(doc!);
        bag.HasErrors.Should().BeFalse(string.Join(";", bag.Items.Select(i => i.Message)));
        return Lowering.Lower(linked!);
    }

    const string Green = """
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
        scenario "produces an analysis"
          given topic-of(ask.text) is "Car Insurance"
          given gpt returns "crowded market"
          when synapse ask(text: "car insurance startup")
          then db has "crowded market"
          and signal ready emitted with summary == "crowded market"
          and counter done == 1
        """;

    [Fact]
    public async Task North_star_scenario_passes()
    {
        var report = await new ScenarioRunner().RunAllAsync(Plan(Green), CancellationToken.None);
        report.AllPassed.Should().BeTrue(string.Join(" | ",
            report.Results.SelectMany(r => r.Failures)));
        report.Results.Should().ContainSingle();
    }

    [Fact]
    public async Task Wrong_expected_value_fails_the_scenario()
    {
        var plan = Plan(Green.Replace("counter done == 1", "counter done == 2"));
        var report = await new ScenarioRunner().RunAllAsync(plan, CancellationToken.None);
        report.AllPassed.Should().BeFalse();
        report.Results[0].Failures.Should().Contain(f => f.Contains("done"));
    }

    [Fact]
    public async Task Duplicate_when_arg_does_not_throw_last_wins()
    {
        const string src = """
            neuron A.X
              using ask   = synapse(A.Req)
              using ready = signal(A.Done)
              on ask:
                emit ready(summary: ask.text)
            scenario "dup when arg"
              when synapse ask(text: "a", text: "b")
              then signal ready emitted with summary == "b"
            """;
        var report = await new ScenarioRunner().RunAllAsync(Plan(src), CancellationToken.None);
        report.AllPassed.Should().BeTrue(string.Join(" | ",
            report.Results.SelectMany(r => r.Failures)));
    }

    [Fact]
    public async Task Mixed_case_author_against_PascalCase_schema_runs_green()
    {
        // E-INO #74. PR #73 made the Linker accept either casing against
        // `ContractSchema.Fields`, but Interpreter.cs:55/89 kept ordinal dict
        // lookups by the author's literal casing — a green link could still
        // produce silent "" at runtime when casing diverged across `when`,
        // handler, and `then`. Lowering must now canonicalize every name
        // bound to a schema field so the runtime sees consistent keys.
        //
        // The author here writes `Text:` in `when` but `ask.text` in the
        // handler and `summary` in both emit and `then`, all against a
        // PascalCase schema (`Text` / `Summary`). Without the lowering
        // canonicalization, `inbound["Text"]` is set, `inbound["text"]`
        // reads "", emit args["summary"] is "", and the assertion fails.
        var bag = new DiagnosticBag();
        const string src = """
            neuron A.X
              using ask   = synapse(A.Req)
              using ready = signal(A.Done)
              on ask:
                emit ready(summary: ask.text)
            scenario "mixed casing"
              when synapse ask(Text: "hello")
              then signal ready emitted with summary == "hello"
            """;
        var doc = new Parser(new Lexer(src, bag).Lex(), bag).ParseDocument();
        var pascalCatalog = new FakeCatalog()
            .With("A.Req", ContractKind.Synapse, "Text")
            .With("A.Done", ContractKind.Synapse, "Summary");
        var linked = new Linker(pascalCatalog, bag).Link(doc!);
        bag.HasErrors.Should().BeFalse(
            string.Join(";", bag.Items.Select(i => $"{i.Code}:{i.Message}")));

        var report = await new ScenarioRunner().RunAllAsync(
            Lowering.Lower(linked!), CancellationToken.None);

        report.AllPassed.Should().BeTrue(string.Join(" | ",
            report.Results.SelectMany(r => r.Failures)));
    }

    [Fact]
    public async Task Missing_when_yields_single_failure_no_spurious_then_failures()
    {
        const string src = """
            neuron A.X
              using ask   = synapse(A.Req)
              using ready = signal(A.Done)
              on ask:
                emit ready(summary: "x")
            scenario "no when"
              then signal ready emitted
            """;
        var report = await new ScenarioRunner().RunAllAsync(Plan(src), CancellationToken.None);
        report.AllPassed.Should().BeFalse();
        report.Results[0].Failures.Should().ContainSingle()
            .Which.Should().Be("scenario has no 'when' step");
    }

    [Fact]
    public async Task Evolved_v5_singularity_scenario_passes()
    {
        const string src = """
            neuron DigitalBrain.Kernel.Settings.SettingsNeuron
              "Hosts the central system settings registry for DigitalBrain."

              ui SettingsCard:
                SDK.DigitalBrain.UI.Panel(padding: 20)
                  SDK.DigitalBrain.UI.VStack(gap: 12, cross: "start")
                    SDK.DigitalBrain.UI.Text("DigitalBrain Settings", variant: "title")
                    SDK.DigitalBrain.UI.Text("Manage secure keys dynamically", variant: "body")
                    
                    SDK.DigitalBrain.UI.Input(label: "Grok API Key", secret: true, key: "grokKey")
                    SDK.DigitalBrain.UI.Button(label: "Save", onTap: clickEvent)

              on Domain.Settings.RequestSetting:
                Domain.Settings.SettingsStore.Get -> Domain.Settings.SettingResult

              on Domain.Settings.UpdateSetting:
                write Domain.Settings.SettingsStore(Scope: UpdateSetting.Scope, Key: UpdateSetting.Key) = UpdateSetting.Value
                emit Domain.Settings.SettingChanged(Scope: UpdateSetting.Scope, Key: UpdateSetting.Key, Value: UpdateSetting.Value)

            test "Setting read":
              mock Domain.Settings.SettingsStore.Get to "dark"
              when Domain.Settings.RequestSetting(Scope: "user", Key: "theme")
              expect Domain.Settings.SettingResult(Value: "dark")
            """;

        var bag = new DiagnosticBag();
        var doc = new Parser(new Lexer(src, bag).Lex(), bag).ParseDocument();
        var catalog = new FakeCatalog()
            .With("Domain.Settings.RequestSetting", ContractKind.Synapse, "Scope", "Key")
            .With("Domain.Settings.SettingResult", ContractKind.Synapse, "Scope", "Key", "Value")
            .With("Domain.Settings.UpdateSetting", ContractKind.Synapse, "Scope", "Key", "Value")
            .With("Domain.Settings.SettingChanged", ContractKind.Synapse, "Scope", "Key", "Value")
            .With("Domain.Settings.SettingsStore", ContractKind.Neuron)
            .With("Domain.Settings.SettingsStore.Get", ContractKind.Neuron);

        var linked = new Linker(catalog, bag).Link(doc!);
        bag.HasErrors.Should().BeFalse(string.Join(";", bag.Items.Select(i => i.Message)));

        var plan = Lowering.Lower(linked!);
        plan.Ui.Should().NotBeNull();
        plan.Ui!.CardName.Should().Be("SettingsCard");

        var report = await new ScenarioRunner().RunAllAsync(plan, CancellationToken.None);
        report.AllPassed.Should().BeTrue(string.Join(" | ", report.Results.SelectMany(r => r.Failures)));
    }
}
