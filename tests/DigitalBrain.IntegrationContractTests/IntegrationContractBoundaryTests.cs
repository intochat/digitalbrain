using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Integrations.Salesforce.Contracts;
using Xunit;

namespace DigitalBrain.IntegrationContractTests;

public sealed class IntegrationContractBoundaryTests
{
    private static readonly Assembly[] ContractAssemblies =
    [
        typeof(GoogleCapabilityIds).Assembly,
        typeof(SalesforceCapabilityIds).Assembly
    ];

    private static readonly IReadOnlyDictionary<string, string[]> AllowedAssemblyReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["DigitalBrain.Integrations.Google.Contracts"] = ["System.Runtime"],
            ["DigitalBrain.Integrations.Salesforce.Contracts"] = ["System.Collections", "System.Runtime", "System.Text.Json"]
        };

    [Fact]
    public void Contract_projects_have_no_package_or_project_dependencies()
    {
        foreach (var path in ContractProjectPaths())
        {
            var project = XDocument.Load(path);
            Assert.Empty(project.Descendants("PackageReference"));
            Assert.Empty(project.Descendants("ProjectReference"));

            using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                Path.GetDirectoryName(path)!,
                "obj",
                "project.assets.json")));
            var restoredDependencies = assets.RootElement
                .GetProperty("libraries")
                .EnumerateObject()
                .Where(static library =>
                {
                    var type = library.Value.GetProperty("type").GetString();
                    return type is "package" or "project";
                })
                .Select(static library => library.Name)
                .ToArray();
            Assert.Empty(restoredDependencies);
        }
    }

    [Fact]
    public void Contract_assemblies_reference_only_system_assemblies()
    {
        foreach (var assembly in ContractAssemblies)
        {
            var actual = assembly.GetReferencedAssemblies()
                .Select(static reference => reference.Name ?? string.Empty)
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(AllowedAssemblyReferences[assembly.GetName().Name!], actual);
        }
    }

    [Fact]
    public void Contract_source_uses_no_host_or_side_effect_apis()
    {
        string[] forbidden =
        [
            "System.IO",
            "System.Net",
            "System.Diagnostics",
            "Environment.",
            "File.",
            "Directory.",
            "HttpClient",
            "Process."
        ];

        foreach (var projectPath in ContractProjectPaths())
        {
            var directory = Path.GetDirectoryName(projectPath)!;
            foreach (var sourcePath in Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly))
            {
                var source = File.ReadAllText(sourcePath);
                Assert.All(forbidden, token => Assert.DoesNotContain(token, source, StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void Contract_async_methods_put_optional_cancellation_last()
    {
        var offenders = ContractAssemblies
            .SelectMany(static assembly => assembly.GetExportedTypes())
            .Where(static type => type.IsInterface)
            .SelectMany(static type => type.GetMethods())
            .Where(static method => typeof(Task).IsAssignableFrom(method.ReturnType))
            .Where(static method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length == 0 ||
                    parameters[^1].ParameterType != typeof(CancellationToken) ||
                    !parameters[^1].IsOptional;
            })
            .Select(static method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Contract_public_surface_is_exact()
    {
        Type[] dataTransferTypes =
        [
            typeof(GmailMessageReadRequest),
            typeof(GmailMessage),
            typeof(GmailMailboxReadRequest),
            typeof(GmailMessageSummary),
            typeof(GmailMailboxPage),
            typeof(GmailSendProposalRequest),
            typeof(GmailSendProposal),
            typeof(SalesforceRecordReference),
            typeof(SalesforceRecordReadRequest),
            typeof(SalesforceRecord),
            typeof(SalesforceUpdateProposalRequest),
            typeof(SalesforceUpdateProposal)
        ];
        Type[] interfaces =
        [
            typeof(IGmailMessageReader),
            typeof(IGmailMailboxReader),
            typeof(IGmailSendProposer),
            typeof(ISalesforceRecordReader),
            typeof(ISalesforceUpdateProposer)
        ];
        var expectedTypes = dataTransferTypes
            .Concat(interfaces)
            .Append(typeof(GoogleCapabilityIds))
            .Append(typeof(SalesforceCapabilityIds))
            .Select(static type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actualTypes = ContractAssemblies
            .SelectMany(static assembly => assembly.GetExportedTypes())
            .Select(static type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedTypes, actualTypes);

        AssertDto<GmailMessageReadRequest>(("MessageId", typeof(string)));
        AssertDto<GmailMessage>(
            ("MessageId", typeof(string)),
            ("ThreadId", typeof(string)),
            ("ReceivedAt", typeof(DateTimeOffset)),
            ("SenderAddress", typeof(string)),
            ("Subject", typeof(string)),
            ("PlainTextBody", typeof(string)));
        AssertDto<GmailMailboxReadRequest>(("Limit", typeof(int)), ("ContinuationToken", typeof(string)));
        AssertDto<GmailMessageSummary>(
            ("MessageId", typeof(string)),
            ("ThreadId", typeof(string)),
            ("ReceivedAt", typeof(DateTimeOffset)),
            ("SenderAddress", typeof(string)),
            ("Subject", typeof(string)));
        AssertDto<GmailMailboxPage>(
            ("Messages", typeof(IReadOnlyList<GmailMessageSummary>)),
            ("ContinuationToken", typeof(string)));
        AssertDto<GmailSendProposalRequest>(
            ("Recipient", typeof(string)),
            ("Subject", typeof(string)),
            ("Body", typeof(string)),
            ("LogicalOperationKey", typeof(string)));
        AssertDto<GmailSendProposal>(
            ("Recipient", typeof(string)),
            ("Subject", typeof(string)),
            ("Body", typeof(string)),
            ("LogicalOperationKey", typeof(string)));
        AssertDto<SalesforceRecordReference>(("ObjectName", typeof(string)), ("RecordId", typeof(string)));
        AssertDto<SalesforceRecordReadRequest>(
            ("Record", typeof(SalesforceRecordReference)),
            ("Fields", typeof(IReadOnlyList<string>)));
        AssertDto<SalesforceRecord>(
            ("Reference", typeof(SalesforceRecordReference)),
            ("Fields", typeof(IReadOnlyDictionary<string, JsonElement>)));
        AssertDto<SalesforceUpdateProposalRequest>(
            ("Record", typeof(SalesforceRecordReference)),
            ("Field", typeof(string)),
            ("NewValue", typeof(JsonElement)),
            ("LogicalOperationKey", typeof(string)));
        AssertDto<SalesforceUpdateProposal>(
            ("Record", typeof(SalesforceRecordReference)),
            ("Field", typeof(string)),
            ("NewValue", typeof(JsonElement)),
            ("LogicalOperationKey", typeof(string)));

        AssertInterface<IGmailMessageReader>("ReadAsync", typeof(Task<GmailMessage>), typeof(GmailMessageReadRequest));
        AssertInterface<IGmailMailboxReader>("ReadAsync", typeof(Task<GmailMailboxPage>), typeof(GmailMailboxReadRequest));
        AssertInterface<IGmailSendProposer>("ProposeAsync", typeof(Task<GmailSendProposal>), typeof(GmailSendProposalRequest));
        AssertInterface<ISalesforceRecordReader>("ReadAsync", typeof(Task<SalesforceRecord>), typeof(SalesforceRecordReadRequest));
        AssertInterface<ISalesforceUpdateProposer>("ProposeAsync", typeof(Task<SalesforceUpdateProposal>), typeof(SalesforceUpdateProposalRequest));
    }

    private static void AssertDto<T>(params (string Name, Type Type)[] expected)
    {
        var type = typeof(T);
        Assert.True(type.IsClass && type.IsSealed);
        var actualProperties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(static property => (property.Name, property.PropertyType))
            .OrderBy(static property => property.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected.OrderBy(static property => property.Name, StringComparer.Ordinal), actualProperties);
        var constructor = Assert.Single(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Equal(expected.Select(static property => property.Type), constructor.GetParameters().Select(static parameter => parameter.ParameterType));
        Assert.DoesNotContain(
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly),
            static method => !method.IsSpecialName);
    }

    private static void AssertInterface<T>(string name, Type returnType, Type requestType)
    {
        var type = typeof(T);
        Assert.True(type.IsInterface && type.IsPublic);
        var method = Assert.Single(type.GetMethods());
        Assert.Equal(name, method.Name);
        Assert.Equal(returnType, method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal([requestType, typeof(CancellationToken)], parameters.Select(static parameter => parameter.ParameterType));
        Assert.True(parameters[1].IsOptional);
    }

    private static string[] ContractProjectPaths()
    {
        var root = RepositoryRoot();
        return
        [
            Path.Combine(root, "integrations", "DigitalBrain.Integrations.Google.Contracts", "DigitalBrain.Integrations.Google.Contracts.csproj"),
            Path.Combine(root, "integrations", "DigitalBrain.Integrations.Salesforce.Contracts", "DigitalBrain.Integrations.Salesforce.Contracts.csproj")
        ];
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Brain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
