using System.Reflection;
using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.V2;
using DigitalBrain.Salesforce;
using DigitalBrain.Ui.Contracts;

namespace DigitalBrain.Tests.Architecture;

public sealed class AsyncContractArchitectureTests
{
    private static readonly Type[] AsyncContractTypes =
    [
        typeof(INeuron),
        typeof(IHandle<>),
        typeof(IIngressNeuron),
        typeof(IAutomationNeuron),
        typeof(IUserSessionNeuron),
        typeof(IFlutterUiNeuron),
        typeof(IV2GmailReadToolGrain),
        typeof(IV2SalesforceReadToolGrain),
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
    public void IHandle_Contract_Requires_CancellationToken_Handler()
    {
        var handleMethods = typeof(IHandle<>)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == nameof(IHandle<Synapse>.HandleAsync))
            .ToArray();

        var method = Assert.Single(handleMethods);
        var parameters = method.GetParameters();

        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
        Assert.True(parameters[1].IsOptional);
    }

    [Fact]
    public void IHandle_Implementations_Do_Not_Keep_OneParameter_HandleAsync()
    {
        var productionAssemblies = new[]
        {
            typeof(Neuron).Assembly,
            typeof(GmailReadNeuron).Assembly,
            typeof(SalesforceReadNeuron).Assembly
        }.Distinct().ToArray();

        var offenders = productionAssemblies
            .SelectMany(LoadableTypes)
            .Where(type => !type.IsAbstract && type.GetInterfaces().Any(IsIHandleInterface))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.Name == nameof(IHandle<Synapse>.HandleAsync))
                .Where(method =>
                {
                    var parameters = method.GetParameters();
                    return parameters.Length == 1 && typeof(Synapse).IsAssignableFrom(parameters[0].ParameterType);
                })
                .Select(MethodId))
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

        Assert.Contains("dotnet_diagnostic.CA1068.severity = error", editorConfig);
        Assert.Contains("dotnet_diagnostic.CA2012.severity = error", editorConfig);
        Assert.Contains("dotnet_diagnostic.CA2016.severity = error", editorConfig);
    }

    private static IEnumerable<MethodInfo> PublicDeclaredMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName);

    private static bool IsValueTask(Type type) =>
        type == typeof(ValueTask) ||
        (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>));

    private static bool IsIHandleInterface(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IHandle<>);

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
    }

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
