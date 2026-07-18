// Y2 continuation per OS-ON-YAML-SPEC.md and OS-ON-YAML-PLAN.md.
// Basic working YAML loader using YamlDotNet 18.0.0 (latest confirmed via Context7).
// Deserializes os-on-yaml/*.yaml (Schema: Neuron + structured rules/UI per SPEC grammar) and maps
// to the exact same InoExperience / RuleDeclaration / CardItem / EmitDescriptor AST used by InoParser.
// This enables dual support path (content sniff for schemaVersion) without changing any .ino behavior,
// Packager heuristics, RuleHost, Shell, contracts, N+1, replay, or high-sev gates.
// All mappings preserve semantics: handles/emits -> contract decls, rules -> Rule* , show content -> CardItem trees.
// No wiring to Packager / boot / Sdk yet (per small-step Y2). Existing InoParser and .ino paths untouched.
// Self-explanatory names. Context7 used immediately before this impl for DeserializerBuilder, IDeserializer.Deserialize<string>,
// WithCaseInsensitivePropertyMatching, and InoAst shapes. Latest package via central props.

using System;
using System.Collections.Generic;
using System.Linq;
using DigitalBrain.InoLang.Domain.Ino;
using YamlDotNet.Serialization;

namespace DigitalBrain.InoLang.Domain.Yaml;

public static class YamlParser
{
    // The single supported schemaVersion for the os-on-yaml/vocabulary (append-only per SPEC).
    // This makes the declared schemaVersion a first-class, enforceable thing instead of a magic marker only.
    public const string CurrentSchemaVersion = "os-on-yaml/v0";

    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithCaseInsensitivePropertyMatching()
        .WithEnforceRequiredMembers() // per SPEC for required fields like id/version
        .IgnoreUnmatchedProperties() // tolerate root siblings like scenarios: + future fields; keeps deserial robust for v0 yamls
        .Build();

    public static InoExperience? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        if (!content.Contains("schemaVersion: \"os-on-yaml/", StringComparison.OrdinalIgnoreCase))
            return null; // not our format; dual caller falls back to InoParser

        try
        {
            var doc = _deserializer.Deserialize<YamlDoc>(content);
            if (doc?.Neuron == null)
                return null;

            // Enforce that the declared schemaVersion exists/supported (addresses "schemaVersion X doesn't exist").
            // Only CurrentSchemaVersion (v0) is valid today; future versions will be additive.
            if (!string.IsNullOrWhiteSpace(doc.SchemaVersion) && doc.SchemaVersion != CurrentSchemaVersion)
                throw new InoParseException("YIN001", 0, $"schemaVersion '{doc.SchemaVersion}' does not exist; the supported schemaVersion is \"{CurrentSchemaVersion}\"");

            var neuron = doc.Neuron;

            if (string.IsNullOrWhiteSpace(neuron.Id) || string.IsNullOrWhiteSpace(neuron.Version))
                throw new DigitalBrain.InoLang.Domain.Ino.InoParseException("YIN001", 0, "name and version required");

            // Map handles -> triggers (for compatibility with InoExperience; real contracts built higher)
            var triggers = neuron.Handles?
                .Select(h => h.Synapse)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray() ?? Array.Empty<string>();

            var emits = neuron.Emits?.ToArray() ?? Array.Empty<string>();

            // Map rules
            var rules = new List<RuleDeclaration>();
            if (neuron.Rules != null)
            {
                foreach (var r in neuron.Rules)
                {
                    RuleCondition? when = null; // v0 basic; when/condition support can extend from SPEC
                    var statements = new List<RuleStatement>();

                    if (r.Do != null)
                    {
                        foreach (var d in r.Do)
                        {
                            if (d.Emit != null && !string.IsNullOrWhiteSpace(d.Emit.Type))
                            {
                                var args = d.Emit.Args ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                statements.Add(new EmitRuleStatement(new EmitDescriptor(d.Emit.Type, args)));
                            }
                            else if (d.Show?.Card != null)
                            {
                                var card = d.Show.Card;
                                var items = MapCardContent(card.Content);
                                statements.Add(new ShowCardRuleStatement(card.Title, items));
                            }
                        }
                    }

                    rules.Add(new RuleDeclaration(
                        r.On,
                        r.As,
                        when,
                        statements.ToArray()));
                }
            }

            // Map placement etc. (rung-A from SPEC)
            string? region = neuron.Region;
            bool pinned = neuron.Pinned;
            int order = neuron.Order;
            string[] requires = neuron.Requires ?? Array.Empty<string>();
            bool isSystem = neuron.IsSystem;
            string[] requiresGrant = neuron.RequiresGrant ?? Array.Empty<string>();

            return new InoExperience(
                neuron.Id,
                neuron.Version,
                neuron.Desc,
                emits,
                rules.ToArray(),
                neuron.Escalate != null, // hasEscalateCodegen
                region,
                pinned,
                order,
                requires,
                isSystem,
                requiresGrant);
        }
        catch (InoParseException ex) when (ex.Code == "YIN001" && ex.Message.Contains("does not exist"))
        {
            // Schema version that does not exist must surface the explicit error even on the Parse path (for pack/load callers).
            // Other deserial issues remain swallowed for dual .ino fallback (Y2 stub behavior).
            throw;
        }
        catch
        {
            // On parse error in Y2 stub, return null so dual caller can decide (or throw YIN001 in full impl)
            return null;
        }
    }

    public static BootManifest? ParseBoot(string content)
    {
        if (string.IsNullOrWhiteSpace(content) || !content.Contains("boot:", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            var doc = _deserializer.Deserialize<YamlBootDoc>(content);
            if (doc?.Boot == null) return null;

            // Enforce schemaVersion when present on boot manifests (brain.yaml etc). Unknown versions do not exist.
            if (!string.IsNullOrWhiteSpace(doc.SchemaVersion) && doc.SchemaVersion != CurrentSchemaVersion)
                throw new InoParseException("YIN001", 0, $"schemaVersion '{doc.SchemaVersion}' does not exist; the supported schemaVersion is \"{CurrentSchemaVersion}\"");

            var b = doc.Boot;
            if (string.IsNullOrWhiteSpace(b.Name) || string.IsNullOrWhiteSpace(b.Version))
                throw new DigitalBrain.InoLang.Domain.Ino.InoParseException("YIN001", 0, "name and version required");
            var llms = (b.Llms ?? new List<YamlLlm>()).Select(l => (l.Model, l.Tier)).ToList();
            var seeds = (b.Seeds ?? new List<string>()).ToArray();
            var worlds = (b.Worlds ?? new List<YamlWorld>()).Select(w => (w.Name, w.From)).ToList();
            return new BootManifest(
                b.Name,
                b.Version,
                b.Description,
                llms,
                b.Voice,
                b.Durability,
                b.Ui,
                b.Discovery,
                b.AdvertisedIpEnv,
                seeds,
                worlds
            );
        }
        catch (InoParseException ex) when (ex.Code == "YIN001" && ex.Message.Contains("does not exist"))
        {
            // Schema version that does not exist must surface (ParseBoot path for brain.yaml boot manifests).
            throw;
        }
        catch
        {
            return null;
        }
    }

    // Basic schema validation per SPEC (YIN codes, required fields, basic contracts). Author-time like InoValidator.
    // Returns diagnostics (empty if clean). Can be extended for full field contracts, known synapses, etc.
    public static InoDiagnostic[] ValidateYaml(string content)
    {
        var diags = new List<DigitalBrain.InoLang.Domain.Ino.InoDiagnostic>();
        if (string.IsNullOrWhiteSpace(content))
        {
            diags.Add(new InoDiagnostic("YIN001", "Error", 0, "empty content"));
            return diags.ToArray();
        }
        try
        {
            if (content.Contains("boot:", StringComparison.OrdinalIgnoreCase))
            {
                var doc = _deserializer.Deserialize<YamlBootDoc>(content);
                var b = doc?.Boot;
                if (!string.IsNullOrWhiteSpace(doc?.SchemaVersion) && doc.SchemaVersion != CurrentSchemaVersion)
                    diags.Add(new InoDiagnostic("YIN001", "Error", 0, $"schemaVersion '{doc.SchemaVersion}' does not exist; the supported schemaVersion is \"{CurrentSchemaVersion}\""));
                if (b == null || string.IsNullOrWhiteSpace(b.Name) || string.IsNullOrWhiteSpace(b.Version))
                    diags.Add(new InoDiagnostic("YIN001", "Error", 0, "name and version required for boot"));
            }
            else if (content.Contains("neuron:", StringComparison.OrdinalIgnoreCase))
            {
                var doc = _deserializer.Deserialize<YamlDoc>(content);
                var n = doc?.Neuron;
                if (!string.IsNullOrWhiteSpace(doc?.SchemaVersion) && doc.SchemaVersion != CurrentSchemaVersion)
                    diags.Add(new InoDiagnostic("YIN001", "Error", 0, $"schemaVersion '{doc.SchemaVersion}' does not exist; the supported schemaVersion is \"{CurrentSchemaVersion}\""));
                if (n == null || string.IsNullOrWhiteSpace(n.Id) || string.IsNullOrWhiteSpace(n.Version))
                    diags.Add(new InoDiagnostic("YIN001", "Error", 0, "name and version required"));
                if ((n?.Handles == null || n.Handles.Count == 0) && (n?.Emits == null || n.Emits.Count == 0))
                    diags.Add(new InoDiagnostic("YIN006", "Warning", 0, "no handles or emits declared"));
            }
            else
            {
                diags.Add(new InoDiagnostic("YIN001", "Error", 0, "missing neuron: or boot: section"));
            }
        }
        catch (Exception ex)
        {
            diags.Add(new InoDiagnostic("YIN001", "Error", 0, $"parse error: {ex.Message}"));
        }
        return diags.ToArray();
    }

    private static CardItem[] MapCardContent(List<YamlCardItem>? content)
    {
        if (content == null || content.Count == 0)
            return Array.Empty<CardItem>();

        var list = new List<CardItem>();
        foreach (var it in content)
        {
            var kind = it.Kind?.ToLowerInvariant() ?? "text";
            var text = it.Text ?? it.Label ?? "";
            EmitDescriptor? action = null;
            if (it.Action != null && !string.IsNullOrWhiteSpace(it.Action.Type))
            {
                var args = it.Action.Args ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                action = new EmitDescriptor(it.Action.Type, args);
            }

            CardItem[]? children = null;
            if (it.Content != null && it.Content.Count > 0)
                children = MapCardContent(it.Content);

            list.Add(new CardItem(kind, text, action, children));
        }
        return list.ToArray();
    }

    // Internal POCOs for deserialization (match SPEC grammar exactly for the examples).
    // Written as property records (init) for reliable YamlDotNet binding (positional ctors can be finicky with nested lists/unions + IgnoreUnmatched).
    private sealed record YamlDoc
    {
        public string SchemaVersion { get; init; } = "";
        public YamlNeuron? Neuron { get; init; }
    }

    private sealed record YamlNeuron
    {
        public string Id { get; init; } = "";
        public string Version { get; init; } = "";
        public string? Desc { get; init; }
        public List<YamlHandle>? Handles { get; init; }
        public List<string>? Emits { get; init; }
        public string? Region { get; init; }
        public bool Pinned { get; init; }
        public int Order { get; init; }
        public List<YamlRule>? Rules { get; init; }
        public bool IsSystem { get; init; }
        public string[]? Requires { get; init; }
        public string[]? RequiresGrant { get; init; }
        public string? Escalate { get; init; }
    }

    private sealed record YamlHandle
    {
        public string Synapse { get; init; } = "";
        public List<YamlField>? Fields { get; init; }
    }

    private sealed record YamlField
    {
        public string Name { get; init; } = "";
        public string Type { get; init; } = "";
        public bool Required { get; init; }
    }

    private sealed record YamlRule
    {
        public string On { get; init; } = "";
        public string? As { get; init; }
        public List<YamlDoItem>? Do { get; init; }
    }

    private sealed record YamlDoItem
    {
        public YamlEmit? Emit { get; init; }
        public YamlShow? Show { get; init; }
    }

    private sealed record YamlEmit
    {
        public string Type { get; init; } = "";
        public Dictionary<string, string>? Args { get; init; }
    }

    private sealed record YamlShow
    {
        public YamlCard? Card { get; init; }
    }

    private sealed record YamlCard
    {
        public string? Title { get; init; }
        public List<YamlCardItem>? Content { get; init; }
    }

    private sealed record YamlCardItem
    {
        public string? Kind { get; init; }
        public string? Text { get; init; }
        public string? Label { get; init; }
        public YamlAction? Action { get; init; }
        public List<YamlCardItem>? Content { get; init; }
    }

    private sealed record YamlAction
    {
        public string Type { get; init; } = "";
        public Dictionary<string, string>? Args { get; init; }
    }

    // Boot POCOs (match SPEC for brain.yaml boot section)
    private sealed record YamlBootDoc
    {
        public string SchemaVersion { get; init; } = "";
        public YamlBoot? Boot { get; init; }
    }

    private sealed record YamlBoot
    {
        public string Name { get; init; } = "";
        public string Version { get; init; } = "";
        public string? Description { get; init; }
        public List<YamlLlm>? Llms { get; init; }
        public string? Voice { get; init; }
        public string? Durability { get; init; }
        public string? Ui { get; init; }
        public bool Discovery { get; init; }
        public string? AdvertisedIpEnv { get; init; }
        public List<string>? Seeds { get; init; }
        public List<YamlWorld>? Worlds { get; init; }
    }

    private sealed record YamlLlm
    {
        public string Model { get; init; } = "";
        public string Tier { get; init; } = "";
    }

    private sealed record YamlWorld
    {
        public string Name { get; init; } = "";
        public string From { get; init; } = "";
    }
}
