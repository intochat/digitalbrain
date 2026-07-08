using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Salesforce;
using System.Reflection;

namespace DigitalBrain.Tests.Architecture;

public sealed class AsyncContractArchitectureTests
{
    private static readonly Type[] AsyncContractTypes =
    [
        typeof(INeuron),
        typeof(IInoNeuron),
        typeof(IIngressNeuron),
        typeof(IGoogleAuthNeuron),
        typeof(ISalesforceAuthNeuron),
        typeof(IConnector),
        typeof(IPackConfigStore),
        typeof(IPackConfigBackingStore),
        typeof(IGmailApiClientFactory),
        typeof(ISalesforceApiClientFactory)
    ];

    [Fact]
    public void Core_Grain_Contract_Does_Not_Return_ValueTask()
    {
        var offenders = PublicDeclaredMethods(typeof(INeuron))
            .Where(method => IsValueTask(method.ReturnType))
            .Select(MethodId)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Async_Public_Contracts_Put_CancellationToken_Last_And_Optional()
    {
        var offenders = AsyncContractTypes
            .SelectMany(PublicDeclaredMethods)
            .Select(method => new
            {
                Method = method,
                CancellationParameters = method.GetParameters()
                    .Select((parameter, index) => new { Parameter = parameter, Index = index })
                    .Where(item => item.Parameter.ParameterType == typeof(CancellationToken))
                    .ToArray()
            })
            .Where(item => item.CancellationParameters.Length > 0)
            .Where(item =>
            {
                var parameters = item.Method.GetParameters();
                var cancellation = item.CancellationParameters.SingleOrDefault();
                return cancellation is null ||
                    cancellation.Index != parameters.Length - 1 ||
                    !cancellation.Parameter.IsOptional;
            })
            .Select(item => MethodId(item.Method))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void EditorConfig_Enables_Cancellation_And_ValueTask_Analyzers()
    {
        var editorConfig = File.ReadAllText(FindRepoFile(".editorconfig"));

        Assert.Contains("dotnet_diagnostic.CA1068.severity = warning", editorConfig);
        Assert.Contains("dotnet_diagnostic.CA2012.severity = warning", editorConfig);
        Assert.Contains("dotnet_diagnostic.CA2016.severity = warning", editorConfig);
    }

    private static IEnumerable<MethodInfo> PublicDeclaredMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName);

    private static bool IsValueTask(Type type) =>
        type == typeof(ValueTask) ||
        (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>));

    private static string MethodId(MethodInfo method) =>
        method.DeclaringType!.Name + "." + method.Name;

    private static string FindRepoFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, fileName);
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {fileName} above {AppContext.BaseDirectory}.");
    }
}
