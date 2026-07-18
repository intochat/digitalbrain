using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DigitalBrain.InoLang.Domain.Ino;

// Ino syntax + semantic validator (privileged directives, known synapses via manifest probe, required grants, etc.).
public static class InoValidator
{
    // Core known synapses for tests / default (extend as more core events added). Real usage passes provider from DispatchManifest + contract bundles.
    private static readonly HashSet<string> CoreKnown = new(StringComparer.OrdinalIgnoreCase)
    {
        "BundleInstalled", "UiSurface", "SetAlarm", "AlarmFired", "WeatherQuery", "WeatherResult",
        "ReviewResult", "ReviewProjectRequest", "AgentRequest", "InstallBundle", "PackExperience",
        "PublishToMarketplace", "SaveFileRequest", "StartWorld", "RestartResource", "StartDistributedApp",
        "KernelTask", "HandlerReacted", "SynapseIncoming", "Markdown", "NeuronTelemetry", "ClientTap"
    };

    private static readonly HashSet<string> Privileged = new(StringComparer.OrdinalIgnoreCase)
    {
        "InstallBundle", "InstallFromMarketplace", "PackExperience", "PublishToMarketplace",
        "SaveFileRequest", "StartWorld", "RestartResource", "StartDistributedApp", "AgentRequest",
        "SelfImproveRequest", "ImprovementProposal", "ClientTap"
    };

    // Basic field map for common ones (INO003). Real probe uses ctor params.
    private static readonly Dictionary<string, HashSet<string>> CoreFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WeatherQuery"] = new() { "City" },
        ["AgentRequest"] = new() { "Prompt" },
        ["AlarmFired"] = new() { "Label", "Minutes" },
        ["ReviewResult"] = new() { "Path", "TodoCount" },
        ["ReviewProjectRequest"] = new() { "Path" },
        ["KernelTask"] = new() { "Progress", "TaskId" },
        ["SynapseIncoming"] = new() { "Payload" },
        ["SetAlarm"] = new() { "Minutes", "Label" }
    };

    public static InoDiagnostic[] Validate(string content, IInoKnownContracts? known = null)
    {
        var diags = new List<InoDiagnostic>();
        InoExperience exp;
        try
        {
            exp = InoParser.Parse(content);
        }
        catch (InoParseException ex)
        {
            diags.Add(new InoDiagnostic(ex.Code, "Error", ex.Line, ex.Message));
            return diags.ToArray();
        }

        var k = known ?? new DefaultKnown();

        // INO006 basic: if triggers header present vs on blocks (for full header .ino)
        // (in pure rule capsules triggers may be omitted; simple check skipped for v0)

        if (exp.Rules.Length == 0)
            diags.Add(new InoDiagnostic("INO006", "Error", 0, "no rules parsed — single-source UI (on: blocks + show card) vanished; grammar mismatch or missing rules in .ino (deny zero-rule for non-descriptor)"));

        foreach (var r in exp.Rules)
        {
            if (!k.IsKnown(r.On))
                diags.Add(new InoDiagnostic("INO002", "Error", 0, $"unknown trigger synapse '{r.On}'"));

            if (r.When != null)
            {
                var fields = k.GetFields(r.On);
                if (fields.Length > 0 && !fields.Contains(r.When.Field, StringComparer.OrdinalIgnoreCase))
                    diags.Add(new InoDiagnostic("INO003", "Error", 0, $"unknown field '{r.When.Field}' on '{r.On}'"));
            }

            foreach (var st in r.Do)
            {
                if (st is EmitRuleStatement e)
                {
                    if (!exp.Emits.Contains(e.Emit.SynapseType, StringComparer.OrdinalIgnoreCase) && !k.IsKnown(e.Emit.SynapseType))
                        diags.Add(new InoDiagnostic("INO004", "Error", 0, $"emit '{e.Emit.SynapseType}' not declared in emits and not known"));

                    if (Privileged.Contains(e.Emit.SynapseType))
                        diags.Add(new InoDiagnostic("INO004", "Error", 0, $"privileged emit '{e.Emit.SynapseType}' denied (deny-by-default)"));
                }
            }
        }

        if (exp.HasEscalateCodegen)
            diags.Add(new InoDiagnostic("INO005", "Warning", 0, "escalate: codegen — full behavior behind L2 (rules provide only subset)"));

        return diags.ToArray();
    }

    public interface IInoKnownContracts
    {
        bool IsKnown(string synapseType);
        string[] GetFields(string synapseType);
    }

    private sealed class DefaultKnown : IInoKnownContracts
    {
        public bool IsKnown(string synapseType) => CoreKnown.Contains(synapseType);

        public string[] GetFields(string synapseType)
        {
            if (CoreFields.TryGetValue(synapseType, out var f)) return f.ToArray();
            // runtime ctor param probe (for loaded synapse records in test/kernel)
            try
            {
                var t = Type.GetType($"DigitalBrain.Protocol.Domain.Events.{synapseType}, DigitalBrain.Protocol")
                     ?? Type.GetType($"DigitalBrain.Os.Domain.Events.{synapseType}, DigitalBrain.Os");
                if (t != null)
                {
                    var ctor = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
                    if (ctor != null)
                        return ctor.GetParameters().Select(p => p.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToArray();
                }
            }
            catch { }
            return Array.Empty<string>();
        }
    }
}
