using System.Text;
using DigitalBrain.InoLang;
using DigitalBrain.InoLang.Ast;
using DigitalBrain.InoLang.Linking;

namespace DigitalBrain.SDK.DigitalBrain.INO;

public sealed record TranspiledProduct(
    string CSharpCode,
    string FeatureSpecs,
    string StepsCode);

public static class InoToCSharpTranspiler
{
    public static TranspiledProduct Transpile(string source, IContractCatalog catalog)
    {
        var compiled = InoCompiler.Compile(source, catalog);
        if (!compiled.Success || compiled.Linked is null)
        {
            var errors = string.Join("\n", compiled.Diagnostics.Select(d => $"[{d.Severity}] Span {d.Span.Start}..{d.Span.End}: {d.Message}"));
            throw new InvalidOperationException($"InoLang Compilation failed:\n{errors}");
        }

        var doc = compiled.Linked.Doc;
        var fqnParts = doc.Fqn.Split('.');
        var className = fqnParts.Last();
        var nsName = string.Join('.', fqnParts.Take(fqnParts.Count() - 1));
        if (string.IsNullOrEmpty(nsName)) nsName = "DigitalBrain.SDK.Generated";

        var cs = TranspileNeuronClass(doc, nsName, className);
        var features = TranspileFeatureSpecs(doc, className);
        var steps = TranspileStepsClass(doc, nsName, className);

        return new TranspiledProduct(cs, features, steps);
    }

    private static string TranspileNeuronClass(NeuronDoc doc, string ns, string className)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using DigitalBrain.Core;");
        sb.AppendLine("using DigitalBrain.Core.Neurons;");
        sb.AppendLine("using DigitalBrain.Runtime.Runtime;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine("using Orleans;");
        sb.AppendLine("using Orleans.Journaling;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"[Orleans.GrainType(\"{doc.Fqn}\")]");

        // Implement IHandle<T> for each inbound synapse
        var inboundUsings = doc.Usings.Where(u => u.Sigil == PortSigil.Synapse).ToList();
        var interfaces = new List<string> { "INeuron", "ICallNeuronTarget" };
        foreach (var u in inboundUsings)
        {
            interfaces.Add($"IHandle<{u.TargetFqn}>");
        }

        sb.AppendLine($"public sealed class {className}Neuron(");
        sb.AppendLine("    [FromKeyedServices(\"incoming\")] IDurableList<Synapse> incoming,");
        sb.AppendLine("    [FromKeyedServices(\"outgoing\")] IDurableList<Synapse> outgoing,");
        sb.AppendLine("    IGrainFactory grains,");
        sb.AppendLine($"    ILogger<{className}Neuron> logger)");
        sb.AppendLine($"    : Neuron(incoming, outgoing, grains, logger),");
        sb.AppendLine($"      {string.Join(",\n      ", interfaces)}");
        sb.AppendLine("{");

        // Declare seam fields
        var callSeams = doc.Usings.Where(u => u.Sigil == PortSigil.Call || u.Sigil == PortSigil.Resource).ToList();
        foreach (var seam in callSeams)
        {
            var keyStr = seam.Key != null ? $"[\"{seam.Key}\"]" : "";
            sb.AppendLine($"    // Seam {seam.Sigil.ToString()}: {seam.Name} -> {seam.TargetFqn}{keyStr}");
        }
        sb.AppendLine();

        // Implement ICallNeuronTarget ($ sigil)
        sb.AppendLine("    public async Task<string> AskAsync(string prompt)");
        sb.AppendLine("    {");
        sb.AppendLine("        Logger.LogInformation(\"Assistant prompt received: {Prompt}\", prompt);");
        sb.AppendLine("        return await Task.FromResult($\"Acknowledged: {prompt}\");");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Implement synapse handlers
        foreach (var handler in doc.Handlers)
        {
            if (handler.Trigger is PortTrigger trigger)
            {
                var matchingUsing = doc.Usings.FirstOrDefault(u => u.Name == trigger.Port);
                if (matchingUsing != null)
                {
                    sb.AppendLine($"    public async Task HandleAsync({matchingUsing.TargetFqn} synapse, CancellationToken cancellationToken)");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        using var activity = Activity.StartActivity(\"handler.{trigger.Port}\");");

                    // Handle predicate filter if any
                    if (handler.Where != null)
                    {
                        var builtin = handler.Where.Subject.Builtin;
                        var field = handler.Where.Subject.Arg switch
                        {
                            FieldAccessExpr f => $"{matchingUsing.TargetFqn}.{f.Field}",
                            _ => "arg"
                        };
                        sb.AppendLine($"        // Filter: where {builtin}({field}) is \"{handler.Where.Expected}\"");
                    }

                    // Body statements
                    foreach (var stmt in handler.Body)
                    {
                        TranspileStatement(stmt, sb, doc.Usings);
                    }

                    sb.AppendLine("    }");
                    sb.AppendLine();
                }
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void TranspileStatement(Stmt stmt, StringBuilder sb, IReadOnlyList<UsingDecl> usings)
    {
        switch (stmt)
        {
            case LogStmt log:
                sb.AppendLine($"        Logger.LogInformation(\"{{Message}}\", {FormatExpression(log.Message)});");
                break;
            case CountStmt count:
                sb.AppendLine($"        Counter(\"{count.Counter}\").Increment(1);");
                break;
            case EmitStmt emit:
                var outUsing = usings.FirstOrDefault(u => u.Name == emit.Port);
                if (outUsing != null)
                {
                    sb.AppendLine($"        await FireSynapseAsync(new {outUsing.TargetFqn}(");
                    sb.AppendLine("            Headers: SynapseFactory.CreateHeader<INeuron, INeuron>(");
                    sb.AppendLine("                senderId: new NeuronId(InstanceId.ToString()),");
                    sb.AppendLine("                receiverId: new NeuronId(Guid.Empty.ToString())");
                    sb.AppendLine("            )");
                    sb.AppendLine("        ), cancellationToken);");
                }
                break;
            case LetAskStmt ask:
                var seamUsing = usings.FirstOrDefault(u => u.Name == ask.Port);
                if (seamUsing != null)
                {
                    sb.AppendLine($"        var {ask.Var} = await Grains.GetGrain<ICallNeuronTarget>(\"{seamUsing.TargetFqn}\").AskAsync({FormatExpression(ask.Prompt)});");
                }
                break;
            case SaveStmt s:
                sb.AppendLine($"        await WriteStateAsync(\"{s.Port}\", {FormatExpression(s.Value)});");
                break;
            case RememberStmt r:
                sb.AppendLine($"        await WriteStateAsync({FormatExpression(r.Text)}, {FormatExpression(r.Value ?? new StringExpr("", r.Span))});");
                break;
        }
    }

    private static string FormatExpression(Expr expr)
    {
        return expr switch
        {
            StringExpr s => $"\"{s.Value}\"",
            NumberExpr n => n.Value.ToString(),
            FieldAccessExpr f => $"synapse.{f.Field}",
            InterpExpr interp => string.Join(" + ", interp.Parts.Select(FormatExpression)),
            _ => "\"\""
        };
    }

    private static string TranspileFeatureSpecs(NeuronDoc doc, string className)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Feature: {className} Behavior Specification");
        if (!string.IsNullOrEmpty(doc.Intent))
        {
            sb.AppendLine($"  {doc.Intent}");
        }
        sb.AppendLine();

        foreach (var scenario in doc.Scenarios)
        {
            sb.AppendLine($"  Scenario: {scenario.Name}");
            foreach (var step in scenario.Steps)
            {
                var line = step switch
                {
                    GivenNeuronReturns g => $"    Given seam {g.Port} returns \"{FormatExpression(g.Value).Trim('"')}\"",
                    GivenPredicate p => $"    Given predicate {p.Subject.Builtin} yields \"{p.Value}\"",
                    WhenInject w => $"    When synapse {w.Port} is injected",
                    ThenSynapseEmitted t => $"    Then signal {t.Port} is emitted",
                    ThenResourceHas r => $"    Then resource {r.Port} should have values",
                    ThenCounter c => $"    Then counter {c.Counter} should equal {c.Value}",
                    _ => "    # Unknown step"
                };
                sb.AppendLine(line);
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string TranspileStepsClass(NeuronDoc doc, string ns, string className)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Reqnroll;");
        sb.AppendLine("using FluentAssertions;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns}.Tests;");
        sb.AppendLine();
        sb.AppendLine("[Binding]");
        sb.AppendLine($"public sealed class {className}Steps");
        sb.AppendLine("{");
        sb.AppendLine("    [Given(@\"^seam (.*) returns \\\"(.*)\\\"$\")]");
        sb.AppendLine("    public void GivenSeamReturns(string port, string value)");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [When(@\"^synapse (.*) is injected$\")]");
        sb.AppendLine("    public void WhenSynapseIsInjected(string port)");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [Then(@\"^signal (.*) is emitted$\")]");
        sb.AppendLine("    public void ThenSignalIsEmitted(string port)");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
