using System.Reflection;
using System.Text.RegularExpressions;
using DigitalBrain;
using Google;
using Google.Contracts;
using Salesforce;
using Salesforce.Contracts;
using Xunit;

namespace DigitalBrain.Tests.Architecture;

public sealed class DurableNeuronGuardTests
{
    [Fact]
    public void Provider_neurons_declare_no_callable_surface_beyond_their_leaf_interfaces()
    {
        AssertProviderSurface(typeof(GmailNeuron), typeof(IGmail));
        AssertProviderSurface(typeof(SalesforceNeuron), typeof(ISalesforce));
    }

    [Fact]
    public void DigitalBrainClient_exposes_only_typed_provider_agnostic_entry_points()
    {
        var methods = typeof(DigitalBrainClient)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToArray();
        var get = Assert.Single(methods);
        Assert.Equal(nameof(DigitalBrainClient.Get), get.Name);
        Assert.True(get.IsGenericMethodDefinition);
        Assert.Empty(get.GetParameters());
        var neuronType = Assert.Single(get.GetGenericArguments());
        Assert.Contains(typeof(INeuron), neuronType.GetGenericParameterConstraints());

        var properties = typeof(DigitalBrainClient)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var conversations = Assert.Single(properties);
        Assert.Equal(nameof(DigitalBrainClient.Conversations), conversations.Name);
        Assert.Equal(typeof(ConversationCollection), conversations.PropertyType);
    }

    [Fact]
    public void Active_product_sources_contain_no_rejected_routing_architecture()
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepositoryFile("Brain.slnx"))!;
        var sourceRoots = new[]
        {
            "hosts",
            "integrations",
            "kernel",
            "modules",
            "samples"
        };
        var forbiddenTokens = new[]
        {
            "INeuron" + "Kind",
            "Kind" + "Catalog",
            "Neuron" + "Address",
            "Dispatch" + "Proxy",
            "INeuron" + "Contract",
            "NeuronContract" + "Attribute",
            "AddBrain" + "Kind",
            "VolatileJournal" + "StorageProvider",
            "IJournalStorage" + "Provider",
            "ConfigurationBound" + "ChatClient",
            "InvokeMcp" + "Tool",
            "ModelContext" + "Protocol"
        };
        var jsonElementBoundary =
            "samples/DigitalBrain.Quickstart/DigitalBrain.Quickstart.TestProvider/Program.cs";
        var genericAskToken = "A" + "sk";
        var jsonElementToken = "Json" + "Element";
        var violations = new List<string>();
        var sourceFiles = sourceRoots
            .Select(path => Path.Combine(repositoryRoot, path))
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(
                path,
                "*",
                SearchOption.AllDirectories))
            .Where(path =>
                Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var sourceDocuments = sourceFiles.ToDictionary(
            path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
            File.ReadAllText,
            StringComparer.Ordinal);

        foreach (var sourceDocument in sourceDocuments)
        {
            var relativePath = sourceDocument.Key;
            var source = sourceDocument.Value;
            violations.AddRange(forbiddenTokens
                .Where(token => source.Contains(token, StringComparison.Ordinal))
                .Select(token => $"{relativePath}: {token}"));
            if (Regex.IsMatch(
                    source,
                    $@"\b{Regex.Escape(genericAskToken)}\b",
                    RegexOptions.CultureInvariant))
                violations.Add($"{relativePath}: {genericAskToken}");
            if (ContainsStringContractSwitch(source))
                violations.Add($"{relativePath}: string contract switch");
            if (ContainsKeyedProviderService(source))
                violations.Add($"{relativePath}: keyed provider service");
            if (!relativePath.Equals(jsonElementBoundary, StringComparison.Ordinal) &&
                source.Contains(jsonElementToken, StringComparison.Ordinal))
            {
                violations.Add($"{relativePath}: {jsonElementToken}");
            }
        }
        violations.AddRange(FindCrossFileKeyedProviderViolations(sourceDocuments));

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("return contractName switch { _ => value };")]
    [InlineData("switch (request.ContractName) { default: break; }")]
    [InlineData("return contractId switch { _ => value };")]
    [InlineData("return surfaceAddress.ContractId switch { _ => value };")]
    [InlineData("var route = request.ContractName; return route switch { _ => value };")]
    [InlineData("var route = request.ContractName;\nreturn route\n    switch { _ => value };")]
    [InlineData("return ((string)request.ContractName) switch { _ => value };")]
    [InlineData("return contracts[index].ContractId switch { _ => value };")]
    public void String_contract_switch_guard_is_not_tied_to_an_exact_identifier(string source)
    {
        Assert.True(ContainsStringContractSwitch(source));
    }

    [Fact]
    public void String_contract_switch_guard_does_not_reject_typed_contract_enums()
    {
        Assert.False(ContainsStringContractSwitch(
            "return ContractMode switch { ContractMode.Strict => value, _ => fallback };"));
    }

    [Fact]
    public void Keyed_provider_guard_is_not_tied_to_a_source_directory()
    {
        Assert.True(ContainsKeyedProviderService(
            "services.AddKeyedSingleton<IChatClient>(name, client);"));
    }

    [Theory]
    [InlineData("services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(name, client);")]
    [InlineData("services.AddKeyedSingleton<ChatClient>(name, client);")]
    public void Keyed_provider_guard_covers_chat_and_embedding_client_types(string source)
    {
        Assert.True(ContainsKeyedProviderService(source));
    }

    [Fact]
    public void Keyed_provider_guard_follows_generic_helpers_across_files()
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Helper.cs"] =
                "internal static void Register<TClient>() { services.AddKeyedSingleton<TClient>(name, client); }",
            ["Registration.cs"] = "Register<IChatClient>();"
        };

        Assert.NotEmpty(FindCrossFileKeyedProviderViolations(sources));
    }

    [Fact]
    public void Keyed_provider_guard_follows_inferred_generic_helper_calls()
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Helper.cs"] =
                "internal static void Register<TClient>(TClient client) { services.AddKeyedSingleton<TClient>(name, client); }",
            ["Registration.cs"] =
                "internal static void Configure(IChatClient chatClient) { Register(chatClient); }"
        };

        Assert.NotEmpty(FindCrossFileKeyedProviderViolations(sources));
    }

    private static void AssertProviderSurface(Type implementation, Type leafInterface)
    {
        var leafMethods = leafInterface
            .GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var implementationMethods = implementation
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToArray();

        Assert.Empty(leafMethods);
        Assert.Empty(implementationMethods);
    }

    private static bool ContainsStringContractSwitch(string source)
    {
        var contractRoutePattern = new Regex(
            @"\bcontract(?:id|name)?\b",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assignments = Regex.Matches(
            source,
            @"\b(?:(?:var|string)\s+)?(?<name>[A-Za-z_]\w*)\s*=\s*(?<value>[^;]+);",
            RegexOptions.CultureInvariant);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (Match assignment in assignments)
            {
                var value = assignment.Groups["value"].Value;
                if (!contractRoutePattern.IsMatch(value) &&
                    !aliases.Any(alias => ContainsIdentifier(value, alias)))
                {
                    continue;
                }

                changed |= aliases.Add(assignment.Groups["name"].Value);
            }
        }

        return EnumerateSwitchSelectors(source).Any(selector =>
            contractRoutePattern.IsMatch(selector) ||
            aliases.Any(alias => ContainsIdentifier(selector, alias)));
    }

    private static bool ContainsKeyedProviderService(string source)
    {
        string[] keyedTokens =
        [
            "AddKeyed",
            "GetKeyed",
            "KeyedService"
        ];
        string[] providerClientTokens =
        [
            "IChatClient",
            "ChatClient",
            "IEmbeddingGenerator",
            "Embedding<",
            "OpenAIClient",
            "IAnthropicClient",
            "AnthropicClient",
            "ModelClient"
        ];

        return keyedTokens.Any(token => source.Contains(token, StringComparison.Ordinal)) &&
            providerClientTokens.Any(token => source.Contains(token, StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> FindCrossFileKeyedProviderViolations(
        IReadOnlyDictionary<string, string> sourceDocuments)
    {
        var helperNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sourceDocuments.Values)
        {
            foreach (Match keyedCall in Regex.Matches(
                         source,
                         @"\b(?:Add|Get)\w*Keyed\w*\s*<\s*(?<typeParameter>T[A-Za-z0-9_]*)\s*>",
                         RegexOptions.CultureInvariant))
            {
                var typeParameter = Regex.Escape(keyedCall.Groups["typeParameter"].Value);
                var declarations = Regex.Matches(
                    source[..keyedCall.Index],
                    $@"\b(?<name>[A-Za-z_]\w*)\s*<[^>\r\n]*\b{typeParameter}\b[^>\r\n]*>\s*\(",
                    RegexOptions.CultureInvariant);
                if (declarations.Count > 0)
                    helperNames.Add(declarations[declarations.Count - 1].Groups["name"].Value);
            }
        }

        var providerTypePattern =
            @"(?:[A-Za-z_]\w*\.)*(?:IChatClient|ChatClient|IEmbeddingGenerator|" +
            @"Embedding|OpenAIClient|IAnthropicClient|AnthropicClient|ModelClient)";
        return helperNames
            .SelectMany(helperName => sourceDocuments
                .Where(document =>
                {
                    if (Regex.IsMatch(
                            document.Value,
                            $@"\b{Regex.Escape(helperName)}\s*<\s*{providerTypePattern}\b",
                            RegexOptions.CultureInvariant))
                    {
                        return true;
                    }

                    var providerVariables = Regex.Matches(
                            document.Value,
                            $@"\b{providerTypePattern}(?:\s*<[^;\r\n=]+>)?[ \t]+(?<name>[A-Za-z_]\w*)",
                            RegexOptions.CultureInvariant)
                        .Select(match => match.Groups["name"].Value)
                        .ToArray();
                    return Regex.Matches(
                            document.Value,
                            $@"\b{Regex.Escape(helperName)}\s*\((?<arguments>[^)]*)\)",
                            RegexOptions.CultureInvariant)
                        .Any(call =>
                            Regex.IsMatch(
                                call.Groups["arguments"].Value,
                                $@"\b{providerTypePattern}\b",
                                RegexOptions.CultureInvariant) ||
                            providerVariables.Any(variable => ContainsIdentifier(
                                call.Groups["arguments"].Value,
                                variable)));
                })
                .Select(document => $"{document.Key}: keyed provider helper {helperName}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateSwitchSelectors(string source)
    {
        foreach (Match keyword in Regex.Matches(
                     source,
                     @"\bswitch\b",
                     RegexOptions.CultureInvariant))
        {
            var next = keyword.Index + keyword.Length;
            while (next < source.Length && char.IsWhiteSpace(source[next]))
                next++;
            if (next < source.Length && source[next] == '(')
            {
                var depth = 1;
                var end = next + 1;
                while (end < source.Length && depth > 0)
                {
                    if (source[end] == '(')
                        depth++;
                    else if (source[end] == ')')
                        depth--;
                    end++;
                }

                if (depth == 0)
                    yield return source[(next + 1)..(end - 1)];
                continue;
            }

            var start = keyword.Index - 1;
            while (start >= 0 && source[start] is not ';' and not '{' and not '}')
                start--;
            yield return source[(start + 1)..keyword.Index];
        }
    }

    private static bool ContainsIdentifier(string source, string identifier)
    {
        return Regex.IsMatch(
            source,
            $@"\b{Regex.Escape(identifier)}\b",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static string FindRepositoryFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not locate {fileName} from the test output directory.");
    }
}
