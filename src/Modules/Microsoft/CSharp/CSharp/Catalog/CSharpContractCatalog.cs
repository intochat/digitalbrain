using System.Reflection;
using System.Text.Json;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Microsoft.CSharp;

public sealed record CSharpContractModule(string Id, string? Directive);

public sealed record CSharpContractCatalogSnapshot(IReadOnlyList<CSharpContractModule> Modules, IReadOnlyList<string> Contracts, string Example, bool Truncated = false);

public sealed class CSharpContractCatalog(IOptions<CSharpOptions> options)
{
    private const int ResponseBudget = 24000;
    private const string ModulePrefix = "DigitalBrain.Modules.";
    private const string ContractsSuffix = ".Contracts";

    public const string Example = """
        #:project /brain/src/Modules/Time/Contracts/DigitalBrain.Modules.Time.Contracts.csproj
        // Add one #:project line per module whose contracts you use (copy them from Directive).
        // DigitalBrain.Client is referenced for you; its namespace and DigitalBrain.Contracts are imported.
        using ITimer = DigitalBrain.Time.Timers.ITimer;
        using DigitalBrain.Time.Timers.Signals;

        await using var brain = await DigitalBrainClient.ConnectAsync(args);
        var timer = brain.Get<ITimer>(brain.Setting("TimerId") ?? "tea");
        await foreach (var tick in brain.On<TimerTick>(timer, brain.Stopping))
        {
            Console.WriteLine($"TimerTick {tick.TimerId} {tick.ObservedAt:O}");
        }
        // Top-level statements run once; loop over brain.On<T>() to keep reacting to signals.
        // Console output is the log. Settings arrive through brain.Setting(name).
        """;

    public CSharpContractCatalogSnapshot Read(IReadOnlyList<string> moduleIds)
    {
        ArgumentNullException.ThrowIfNull(moduleIds);
        var installed = ContractAssemblies().ToDictionary(ModuleId, StringComparer.OrdinalIgnoreCase);
        var unknown = moduleIds.Where(id => !installed.ContainsKey(id)).ToArray();
        if (unknown.Length > 0)
        { throw new ArgumentException($"Unknown module IDs: {string.Join(", ", unknown)}. Installed module IDs: {string.Join(", ", installed.Keys.Order(StringComparer.Ordinal))}.", nameof(moduleIds)); }
        var modules = installed.OrderBy(module => module.Key, StringComparer.Ordinal)
            .Select(module => new CSharpContractModule(module.Key, Directive(module.Value))).ToArray();
        // Discovery with no modules selected lists module ids and the kernel's own contracts only.
        var contracts = moduleIds.Select(id => installed[id]).Prepend(typeof(INeuron).Assembly).Distinct().SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.IsInterface && typeof(INeuron).IsAssignableFrom(type) && type != typeof(INeuron) || typeof(Signal).IsAssignableFrom(type) && type != typeof(Signal)
                || type == typeof(IDigitalBrain))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(Describe)
            .ToList();
        var snapshot = new CSharpContractCatalogSnapshot(modules, contracts, Example);
        while (JsonSerializer.Serialize(snapshot).Length > ResponseBudget && contracts.Count > 0)
        {
            contracts.RemoveAt(contracts.Count - 1);
            snapshot = snapshot with { Contracts = [.. contracts], Truncated = true };
        }
        return snapshot;
    }

    private static string ModuleId(Assembly assembly)
    {
        var name = assembly.GetName().Name!;
        if (name == typeof(INeuron).Assembly.GetName().Name) { return "kernel"; }
        return name[ModulePrefix.Length..^ContractsSuffix.Length].ToLowerInvariant();
    }

    private string? Directive(Assembly assembly)
    {
        if (options.Value.SourceRoot is not { } sourceRoot) { return null; }
        var project = Directory.EnumerateFiles(Path.Combine(sourceRoot, "src"), assembly.GetName().Name + ".csproj", SearchOption.AllDirectories).FirstOrDefault();
        return project is null ? null
            : "#:project " + CSharpSandbox.SourceMount + "/" + Path.GetRelativePath(sourceRoot, project).Replace('\\', '/');
    }

    private static IEnumerable<Assembly> ContractAssemblies()
        => Directory.GetFiles(AppContext.BaseDirectory, ModulePrefix + "*" + ContractsSuffix + ".dll")
            .Select(path => Assembly.Load(AssemblyName.GetAssemblyName(path)))
            .Prepend(typeof(INeuron).Assembly);

    private static string Describe(Type type)
    {
        var members = type.IsInterface
            ? type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(method =>
                TypeName(method.ReturnType) + " " + method.Name
                + (method.IsGenericMethod ? "<" + string.Join(",", method.GetGenericArguments().Select(TypeName)) + ">" : "")
                + "(" + string.Join(", ", method.GetParameters().Select(parameter => TypeName(parameter.ParameterType) + " " + parameter.Name + (parameter.HasDefaultValue ? " = default" : ""))) + ")")
            : type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(property => TypeName(property.PropertyType) + " " + property.Name);
        return type.FullName + " { " + string.Join("; ", members) + " }";
    }

    private static string TypeName(Type type) => type.IsGenericType
        ? type.Name.Split('`')[0] + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">" : type.Name;
}
