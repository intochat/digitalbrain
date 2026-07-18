using System.Text;
using System.Text.RegularExpressions;

namespace DigitalBrain.V2.Ino;

public sealed class InoParser
{
    private static readonly Regex NeuronDeclaration = new(
        @"^neuron\s+(?<fqn>[A-Za-z_][A-Za-z0-9_.]*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SynapseUsing = new(
        @"^using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*synapse\((?<fqn>[A-Za-z_][A-Za-z0-9_.]*)\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NeuronUsing = new(
        @"^using\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*neuron\((?<fqn>[A-Za-z_][A-Za-z0-9_.]*)\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BroadcastsDeclaration = new(
        @"^broadcasts\s+(?<aliases>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HandlesDeclaration = new(
        @"^handles\s+(?<aliases>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StateDeclaration = new(
        @"^state\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<type>[A-Za-z_][A-Za-z0-9_.]*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HandlerDeclaration = new(
        @"^on\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*:$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ScenarioDeclaration = new(
        @"^scenario\s+""(?<description>[^""]+)""\s*:?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ScenarioStep = new(
        @"^(?<keyword>given|when|then|and|but)\s+(?<text>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SetState = new(
        @"^set\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Emit = new(
        @"^emit\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\((?<args>.*)\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Reply = new(
        @"^reply\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\((?<args>.*)\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Ask = new(
        @"^ask\s+(?<target>[A-Za-z_][A-Za-z0-9_]*)\s+to\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\((?<args>.*)\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public InoProgram Parse(string source)
    {
        var lines = Normalize(source)
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .ToArray();

        if (lines.Length == 0)
        {
            throw new InoException("The .ino source is empty.");
        }

        var neuron = NeuronDeclaration.Match(lines[0].Text.Trim());
        if (!neuron.Success)
        {
            throw new InoException("The .ino source must start with 'neuron <FQN>'.");
        }

        var synapses = new List<InoSynapsePort>();
        var neurons = new List<InoNeuronPort>();
        var broadcasts = new List<string>();
        var handles = new List<string>();
        var states = new List<InoState>();
        var handlers = new List<InoHandler>();
        var scenarios = new List<InoScenario>();

        for (var index = 1; index < lines.Length;)
        {
            var text = lines[index].Text.Trim();

            var synapseUsing = SynapseUsing.Match(text);
            if (synapseUsing.Success)
            {
                synapses.Add(new InoSynapsePort(synapseUsing.Groups["alias"].Value, synapseUsing.Groups["fqn"].Value));
                index++;
                continue;
            }

            var neuronUsing = NeuronUsing.Match(text);
            if (neuronUsing.Success)
            {
                neurons.Add(new InoNeuronPort(neuronUsing.Groups["alias"].Value, neuronUsing.Groups["fqn"].Value));
                index++;
                continue;
            }

            var broadcastsMatch = BroadcastsDeclaration.Match(text);
            if (broadcastsMatch.Success)
            {
                broadcasts.AddRange(SplitAliases(broadcastsMatch.Groups["aliases"].Value));
                index++;
                continue;
            }

            var handlesMatch = HandlesDeclaration.Match(text);
            if (handlesMatch.Success)
            {
                handles.AddRange(SplitAliases(handlesMatch.Groups["aliases"].Value));
                index++;
                continue;
            }

            var state = StateDeclaration.Match(text);
            if (state.Success)
            {
                states.Add(new InoState(state.Groups["name"].Value, state.Groups["type"].Value));
                index++;
                continue;
            }

            var handler = HandlerDeclaration.Match(text);
            if (handler.Success)
            {
                var indent = lines[index].Indent;
                var body = new List<InoStatement>();
                index++;
                while (index < lines.Length && lines[index].Indent > indent)
                {
                    body.Add(ParseStatement(lines[index].Text.Trim(), lines[index].LineNumber));
                    index++;
                }

                handlers.Add(new InoHandler(handler.Groups["alias"].Value, body.ToArray()));
                continue;
            }

            if (text == "ui:")
            {
                var indent = lines[index].Indent;
                index++;
                while (index < lines.Length && lines[index].Indent > indent)
                {
                    index++;
                }

                continue;
            }

            var scenario = ScenarioDeclaration.Match(text);
            if (scenario.Success)
            {
                var indent = lines[index].Indent;
                var steps = new List<InoScenarioStep>();
                index++;
                while (index < lines.Length && lines[index].Indent > indent)
                {
                    steps.Add(ParseScenarioStep(lines[index].Text.Trim(), lines[index].LineNumber));
                    index++;
                }

                scenarios.Add(new InoScenario(scenario.Groups["description"].Value, steps.ToArray()));
                continue;
            }

            if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            {
                index++;
                continue;
            }

            throw new InoException($"Unsupported declaration on line {lines[index].LineNumber}: {text}");
        }

        var program = new InoProgram(
            neuron.Groups["fqn"].Value,
            synapses.ToArray(),
            neurons.ToArray(),
            broadcasts.Distinct(StringComparer.Ordinal).ToArray(),
            handles.Distinct(StringComparer.Ordinal).ToArray(),
            states.ToArray(),
            handlers.ToArray(),
            scenarios.ToArray());

        Validate(program);
        return program;
    }

    private static InoStatement ParseStatement(string text, int lineNumber)
    {
        var set = SetState.Match(text);
        if (set.Success)
        {
            return new SetStateStatement(set.Groups["name"].Value, ParseExpression(set.Groups["value"].Value));
        }

        var emit = Emit.Match(text);
        if (emit.Success)
        {
            return new EmitStatement(emit.Groups["alias"].Value, ParseArguments(emit.Groups["args"].Value, lineNumber));
        }

        var ask = Ask.Match(text);
        if (ask.Success)
        {
            return new AskStatement(
                ask.Groups["target"].Value,
                ask.Groups["alias"].Value,
                ParseArguments(ask.Groups["args"].Value, lineNumber));
        }

        var reply = Reply.Match(text);
        if (reply.Success)
        {
            return new ReplyStatement(reply.Groups["alias"].Value, ParseArguments(reply.Groups["args"].Value, lineNumber));
        }

        throw new InoException($"Unsupported statement on line {lineNumber}: {text}");
    }

    private static InoScenarioStep ParseScenarioStep(string text, int lineNumber)
    {
        var match = ScenarioStep.Match(text);
        if (!match.Success)
        {
            throw new InoException($"Invalid scenario step on line {lineNumber}: {text}");
        }

        return new InoScenarioStep(
            match.Groups["keyword"].Value.ToLowerInvariant(),
            match.Groups["text"].Value.Trim());
    }

    private static InoArgument[] ParseArguments(string text, int lineNumber)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return SplitArguments(text)
            .Select(segment =>
            {
                var colon = segment.IndexOf(':');
                if (colon <= 0)
                {
                    throw new InoException($"Invalid argument on line {lineNumber}: {segment}");
                }

                return new InoArgument(
                    segment[..colon].Trim(),
                    ParseExpression(segment[(colon + 1)..].Trim()));
            })
            .ToArray();
    }

    internal static InoExpression ParseExpression(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return new StringLiteralExpression(Unescape(value[1..^1]));
        }

        var dot = value.IndexOf('.');
        if (dot > 0 && dot < value.Length - 1)
        {
            return new FieldExpression(value[..dot], value[(dot + 1)..]);
        }

        return new RawLiteralExpression(value);
    }

    private static void Validate(InoProgram program)
    {
        var synapseAliases = program.Synapses.Select(port => port.Alias).ToHashSet(StringComparer.Ordinal);
        var neuronAliases = program.Neurons.Select(port => port.Alias).ToHashSet(StringComparer.Ordinal);
        var stateNames = program.States.Select(state => state.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var alias in program.Broadcasts.Concat(program.Handles))
        {
            if (!synapseAliases.Contains(alias))
            {
                throw new InoException($"Unknown synapse alias '{alias}' in wiring declaration.");
            }
        }

        foreach (var handler in program.Handlers)
        {
            if (!program.Handles.Contains(handler.SynapseAlias, StringComparer.Ordinal))
            {
                throw new InoException($"Handler '{handler.SynapseAlias}' is missing from handles.");
            }

            foreach (var statement in handler.Body)
            {
                switch (statement)
                {
                    case SetStateStatement set when !stateNames.Contains(set.Name):
                        throw new InoException($"Unknown state '{set.Name}'.");
                    case EmitStatement emit when !program.Broadcasts.Contains(emit.SynapseAlias, StringComparer.Ordinal):
                        throw new InoException($"Emit '{emit.SynapseAlias}' is missing from broadcasts.");
                    case AskStatement ask when !program.Broadcasts.Contains(ask.SynapseAlias, StringComparer.Ordinal):
                        throw new InoException($"Ask '{ask.SynapseAlias}' is missing from broadcasts.");
                    case AskStatement ask when !neuronAliases.Contains(ask.TargetAlias):
                        throw new InoException($"Unknown neuron target '{ask.TargetAlias}'.");
                    case ReplyStatement reply when !program.Broadcasts.Contains(reply.SynapseAlias, StringComparer.Ordinal):
                        throw new InoException($"Reply '{reply.SynapseAlias}' is missing from broadcasts.");
                }
            }
        }

        if (program.Handlers.Length == 0)
        {
            throw new InoException("At least one handler is required.");
        }
    }

    private static IEnumerable<SourceLine> Normalize(string source)
    {
        var lineNumber = 0;
        foreach (var raw in source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            lineNumber++;
            var expanded = raw.Replace("\t", "  ", StringComparison.Ordinal);
            var stripped = StripInlineComment(expanded).TrimEnd();
            yield return new SourceLine(lineNumber, stripped.TakeWhile(c => c == ' ').Count(), stripped);
        }
    }

    private static string StripInlineComment(string line)
    {
        var builder = new StringBuilder();
        var inString = false;
        var escaped = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (escaped)
            {
                builder.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                builder.Append(c);
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                builder.Append(c);
                continue;
            }

            if (!inString && c == '#')
            {
                break;
            }

            if (!inString && c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                break;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static string[] SplitAliases(string value) =>
        value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<string> SplitArguments(string value)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        var inString = false;
        var escaped = false;

        foreach (var c in value)
        {
            if (escaped)
            {
                builder.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                builder.Append(c);
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                builder.Append(c);
                continue;
            }

            if (c == ',' && !inString)
            {
                result.Add(builder.ToString().Trim());
                builder.Clear();
                continue;
            }

            builder.Append(c);
        }

        if (builder.Length > 0)
        {
            result.Add(builder.ToString().Trim());
        }

        return result;
    }

    private static string Unescape(string value) =>
        value.Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);

    private sealed record SourceLine(int LineNumber, int Indent, string Text);
}
