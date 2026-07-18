namespace DigitalBrain.Runtime.Neurons;

using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using Orleans.Runtime;
using DigitalBrain.Runtime.Diagnostics;
using DigitalBrain.Runtime;

public static class SynapseFactory
{
    private static readonly ActivitySource Activity = DigitalBrainTelemetry.Source;

    // Auto-resolves both TCaller and TReceiver using generic constraints
    public static SynapseMetadata CreateHeader<TCaller, TReceiver>(
        NeuronId callerId,
        NeuronId receiverId,
        CausationId? causationId = null)
        where TCaller : INeuron
        where TReceiver : INeuron
    {
        return CreateHeader(
            callerId,
            typeof(TCaller).Name,
            receiverId,
            typeof(TReceiver).Name,
            causationId
        );
    }

    public static SynapseMetadata CreateHeader(
        NeuronId callerId,
        string callerType,
        NeuronId receiverId,
        string receiverType,
        CausationId? causationId = null)
    {
        return new SynapseMetadata(
            SynapseId: SynapseId.New(),
            CorrelationId: ResolveCorrelationId(),
            CausationId: causationId,
            CallerNeuronId: callerId,
            CallerNeuronType: callerType,
            ReceiverNeuronId: receiverId,
            ReceiverNeuronType: receiverType,
            Timestamp: DateTimeOffset.UtcNow,
            Traceparent: System.Diagnostics.Activity.Current?.Id,
            Tracestate: System.Diagnostics.Activity.Current?.TraceStateString
        );
    }

    private static CorrelationId ResolveCorrelationId()
    {
        var v = RequestContext.Get("DigitalBrain.CorrelationId");
        return v switch
        {
            Guid g => new CorrelationId(g),
            string s when Guid.TryParse(s, out var parsed) => new CorrelationId(parsed),
            _ => CorrelationId.New()
        };
    }

    public static Synapse? CreateSynapse(string fqn, IReadOnlyDictionary<string, string> args)
    {
        Type? type = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(fqn);
            if (type != null) break;
        }

        if (type == null) return null;
        if (!typeof(Synapse).IsAssignableFrom(type)) return null;

        var ctors = type.GetConstructors();
        if (ctors.Length == 0) return null;

        var ctor = ctors.OrderByDescending(c => c.GetParameters().Length).First();
        var parameters = ctor.GetParameters();
        var ctorArgs = new object?[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var paramType = param.ParameterType;

            string? valStr = null;
            if (args.TryGetValue(param.Name ?? string.Empty, out var exactVal))
            {
                valStr = exactVal;
            }
            else
            {
                var matchingKey = args.Keys.FirstOrDefault(k => string.Equals(k, param.Name, StringComparison.OrdinalIgnoreCase));
                if (matchingKey != null)
                {
                    valStr = args[matchingKey];
                }
            }

            if (valStr != null)
            {
                ctorArgs[i] = CoerceValue(valStr, paramType);
            }
            else
            {
                if (param.HasDefaultValue)
                {
                    ctorArgs[i] = param.DefaultValue;
                }
                else
                {
                    ctorArgs[i] = paramType.IsValueType ? Activator.CreateInstance(paramType) : null;
                }
            }
        }

        var instance = ctor.Invoke(ctorArgs) as Synapse;
        if (instance == null) return null;

        var ctorParamNames = new HashSet<string>(
            parameters.Select(p => p.Name ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (prop.CanWrite && !ctorParamNames.Contains(prop.Name))
            {
                string? valStr = null;
                if (args.TryGetValue(prop.Name, out var exactVal))
                {
                    valStr = exactVal;
                }
                else
                {
                    var matchingKey = args.Keys.FirstOrDefault(k => string.Equals(k, prop.Name, StringComparison.OrdinalIgnoreCase));
                    if (matchingKey != null)
                    {
                        valStr = args[matchingKey];
                    }
                }

                if (valStr != null)
                {
                    var coerced = CoerceValue(valStr, prop.PropertyType);
                    prop.SetValue(instance, coerced);
                }
            }
        }

        return instance;
    }

    private static object? CoerceValue(string val, Type type)
    {
        if (type == typeof(string))
        {
            return val;
        }
        if (type == typeof(bool))
        {
            if (string.Equals(val, "true", StringComparison.OrdinalIgnoreCase) || val == "1") return true;
            if (string.Equals(val, "false", StringComparison.OrdinalIgnoreCase) || val == "0") return false;
            return !string.IsNullOrEmpty(val);
        }
        if (type == typeof(Guid))
        {
            return Guid.TryParse(val, out var g) ? g : Guid.Empty;
        }
        if (type.IsEnum)
        {
            return Enum.TryParse(type, val, true, out var result) ? result : Enum.GetValues(type).GetValue(0);
        }
        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>) || type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) || type.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize(val, type);
            }
            catch
            {
                return Activator.CreateInstance(type);
            }
        }
        if (type.IsArray)
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize(val, type);
            }
            catch
            {
                return Array.CreateInstance(type.GetElementType()!, 0);
            }
        }

        try
        {
            return Convert.ChangeType(val, type, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize(val, type);
            }
            catch
            {
                return type.IsValueType ? Activator.CreateInstance(type) : null;
            }
        }
    }
}

