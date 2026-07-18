using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DigitalBrain.Protocol.Domain.Events;

namespace DigitalBrain.InoLang.Domain.Ino;

// Pure interpreter: RuleDeclaration.Execute produces EmittedIntent (ShowCard/Emit). RuleHostNeuron materializes to real Synapse/UiSurface.
public static class RuleInterpreter
{
    public static EmittedIntent[] Execute(RuleDeclaration rule, Synapse incoming)
    {
        if (!string.Equals(rule.On, incoming.GetType().Name, StringComparison.OrdinalIgnoreCase))
            return Array.Empty<EmittedIntent>();

        var alias = rule.Alias ?? rule.On;
        var aliasValues = BuildAliasMap(alias, incoming);

        if (rule.When != null && !EvalCondition(rule.When, aliasValues, incoming))
            return Array.Empty<EmittedIntent>();

        var intents = new List<EmittedIntent>();
        foreach (var st in rule.Do)
        {
            if (st is EmitRuleStatement e)
            {
                var args = EvalArgs(e.Emit.Args, aliasValues, incoming);
                intents.Add(new EmitIntent(e.Emit.SynapseType, args));
            }
            else if (st is ShowCardRuleStatement s)
            {
                var items = s.Items.Select(it => EvalCardItem(it, aliasValues, incoming)).ToArray();
                intents.Add(new ShowCardIntent(s.Title, items));
            }
        }
        return intents.ToArray();
    }

    private static CardItem EvalCardItem(CardItem it, Dictionary<string, object> aliasValues, Synapse incoming)
    {
        var text = EvalTemplate(it.Text, aliasValues, incoming);
        EmitDescriptor? action = null;
        if (it.Action != null)
        {
            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in it.Action.Args)
            {
                args[kv.Key] = EvalTemplate(kv.Value, aliasValues, incoming);
            }
            action = new EmitDescriptor(it.Action.SynapseType, args);
        }
        var children = it.Children?.Select(ch => EvalCardItem(ch, aliasValues, incoming)).ToArray();
        return new CardItem(it.Kind, text, action, children);
    }

    private static string EvalTemplate(string template, Dictionary<string, object> aliasValues, Synapse incoming)
    {
        if (aliasValues == null) return template;
        var result = template;
        foreach (var kv in aliasValues)
        {
            result = result.Replace("{" + kv.Key + "}", kv.Value?.ToString() ?? string.Empty);
        }
        return result;
    }

    private static Dictionary<string, object> BuildAliasMap(string alias, Synapse incoming)
    {
        var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var t = incoming.GetType();
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.Name == "Metadata") continue;
            try
            {
                var v = p.GetValue(incoming);
                if (v != null) map[$"{alias}.{p.Name}"] = v;
            }
            catch { }
        }
        // also expose bare fields for convenience in conditions/args when no alias used
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.Name == "Metadata") continue;
            try { var v = p.GetValue(incoming); if (v != null) map[p.Name] = v; } catch { }
        }
        return map;
    }

    private static bool EvalCondition(RuleCondition c, Dictionary<string, object> alias, Synapse incoming)
    {
        object? left = ResolveValue(c.Field, alias, incoming);
        object? right = ParseLiteral(c.Value);
        if (left == null || right == null) return false;

        return c.Op switch
        {
            "==" => Equals(Coerce(left, right), right),
            "!=" => !Equals(Coerce(left, right), right),
            "contains" => (left?.ToString() ?? "").Contains(right?.ToString() ?? "", StringComparison.OrdinalIgnoreCase),
            "startsWith" => (left?.ToString() ?? "").StartsWith(right?.ToString() ?? "", StringComparison.OrdinalIgnoreCase),
            ">" => Compare(left, right) > 0,
            "<" => Compare(left, right) < 0,
            _ => false
        };
    }

    private static object? ResolveValue(string fieldOrTemplate, Dictionary<string, object> alias, Synapse incoming)
    {
        if (alias.TryGetValue(fieldOrTemplate, out var v)) return v;
        // bare field
        if (alias.TryGetValue(fieldOrTemplate, out v)) return v;
        var t = incoming.GetType();
        var p = t.GetProperty(fieldOrTemplate, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return p?.GetValue(incoming);
    }

    private static object? ParseLiteral(string s)
    {
        if (int.TryParse(s, out var i)) return i;
        if (bool.TryParse(s, out var b)) return b;
        return s.Trim('"');
    }

    private static object? Coerce(object left, object right)
    {
        if (left is string ls && right is string rs) return ls;
        if (left is int li && right is int) return li;
        if (left is int li2 && int.TryParse(right.ToString(), out var ri)) return ri;
        return left;
    }

    private static int Compare(object a, object b)
    {
        if (a is IComparable ca && b is IComparable cb) return ca.CompareTo(cb);
        return string.Compare(a?.ToString(), b?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, object> EvalArgs(Dictionary<string, string> exprs, Dictionary<string, object> alias, Synapse incoming)
    {
        var res = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in exprs)
        {
            var val = kv.Value;
            if (val.StartsWith("{") && val.EndsWith("}"))
            {
                var inner = val.Trim('{', '}');
                res[kv.Key] = ResolveValue(inner, alias, incoming) ?? val;
            }
            else
            {
                res[kv.Key] = ParseLiteral(val) ?? val;
            }
        }
        return res;
    }

    // Intent types (pure, host materializes)
    public abstract record EmittedIntent;

    public sealed record EmitIntent(string SynapseType, Dictionary<string, object> Args) : EmittedIntent;

    public sealed record ShowCardIntent(string? Title, CardItem[] Items) : EmittedIntent;
}

// Lightweight binder (cached ctor param info + construction). Called by RuleHost at install/execute boundary, not per synapse in hot path.
public static class SynapseBinder
{
    private static readonly Dictionary<string, (ConstructorInfo Ctor, ParameterInfo[] Params)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Synapse? TryCreate(string typeName, Dictionary<string, object> values)
    {
        if (!Cache.TryGetValue(typeName, out var info))
        {
            var t = Type.GetType($"DigitalBrain.Protocol.Domain.Events.{typeName}, DigitalBrain.Protocol")
                 ?? Type.GetType($"DigitalBrain.Os.Domain.Events.{typeName}, DigitalBrain.Os");
            if (t == null)
            {
                t = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => {
                        try { return a.GetTypes(); }
                        catch { return Array.Empty<Type>(); }
                    })
                    .FirstOrDefault(x => typeof(Synapse).IsAssignableFrom(x) && string.Equals(x.Name, typeName, StringComparison.OrdinalIgnoreCase));
            }
            if (t == null)
            {
                var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in values)
                {
                    payload[kv.Key] = kv.Value?.ToString() ?? "";
                }
                return new DynamicSynapse(typeName, payload);
            }
            var ctor = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            if (ctor == null) return null;
            info = (ctor, ctor.GetParameters());
            Cache[typeName] = info;
        }

        var args = new object[info.Params.Length];
        for (int i = 0; i < info.Params.Length; i++)
        {
            var p = info.Params[i];
            var matchedKey = values.Keys.FirstOrDefault(k => string.Equals(k, p.Name, StringComparison.OrdinalIgnoreCase));
            if (matchedKey != null && values.TryGetValue(matchedKey, out var v))
                args[i] = ConvertValue(v, p.ParameterType);
            else
                args[i] = p.HasDefaultValue ? p.DefaultValue! : Activator.CreateInstance(p.ParameterType)!;
        }
        try
        {
            return (Synapse)info.Ctor.Invoke(args);
        }
        catch
        {
            return null;
        }
    }

    private static object ConvertValue(object v, Type target)
    {
        if (target == typeof(string)) return v?.ToString() ?? "";
        if (target == typeof(int) && int.TryParse(v?.ToString(), out var i)) return i;
        if (target == typeof(bool) && bool.TryParse(v?.ToString(), out var b)) return b;
        if (v != null && target.IsAssignableFrom(v.GetType())) return v;
        return v ?? Activator.CreateInstance(target)!;
    }
}
