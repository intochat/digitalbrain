using System.Reflection;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class KernelOutboxWakeupContracts(ModuleFixture fixture)
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    [Fact]
    public void OutboxReminderOwnershipIsComposedBehindHiddenContracts()
    {
        var kernel = typeof(Neuron).Assembly;
        var violations = new List<string>();

        if (typeof(Neuron).GetInterfaces().Contains(typeof(IRemindable)))
        {
            violations.Add("Neuron still implements IRemindable.");
        }

        if (typeof(Neuron).GetMethod("ReceiveReminder") is not null)
        {
            violations.Add("Neuron still exposes ReceiveReminder.");
        }

        var drain = RequireType(
            kernel,
            "DigitalBrain.Kernel.IOutboxDrain",
            violations);
        var wakeup = RequireType(
            kernel,
            "DigitalBrain.Kernel.IOutboxWakeup",
            violations);
        var implementation = RequireType(
            kernel,
            "DigitalBrain.Kernel.OutboxWakeup",
            violations);

        ValidateContract(
            drain,
            "db.outbox-drain",
            ["Drain"],
            violations);
        ValidateContract(
            wakeup,
            "db.outbox-wakeup",
            ["Arm", "Disarm"],
            violations);

        if (implementation is not null)
        {
            if (implementation.IsPublic || !implementation.IsSealed)
            {
                violations.Add("OutboxWakeup must be internal and sealed.");
            }

            if (!implementation.GetInterfaces().Contains(typeof(IRemindable)))
            {
                violations.Add("OutboxWakeup must implement IRemindable.");
            }

            if (!string.Equals(
                    AttributeArgument(
                        implementation,
                        "GrainTypeAttribute"),
                    "db-outbox-wakeup",
                    StringComparison.Ordinal))
            {
                violations.Add(
                    "OutboxWakeup must keep grain type 'db-outbox-wakeup'.");
            }
        }

        var exportedReminderVocabulary = kernel
            .GetExportedTypes()
            .Where(type => type.Name is
                "IOutboxDrain" or
                "IOutboxWakeup" or
                "OutboxWakeup")
            .Select(type => type.FullName)
            .ToArray();
        if (exportedReminderVocabulary.Length != 0)
        {
            violations.Add(
                "Kernel exported reminder vocabulary: "
                + string.Join(", ", exportedReminderVocabulary));
        }

        ValidatePrivateReminderOwner(
            Path.Combine(
                RepositoryRoot,
                "modules",
                "DigitalBrain.Modules.Tasks",
                "TaskNeuron.cs"),
            "TaskNeuron",
            violations);
        ValidatePrivateReminderOwner(
            Path.Combine(
                RepositoryRoot,
                "modules",
                "DigitalBrain.Modules.AI",
                "GroupChat.cs"),
            "GroupChat",
            violations);
        ValidateNeuronWakeupComposition(
            Path.Combine(
                RepositoryRoot,
                "src",
                "DigitalBrain.Kernel"),
            violations);

        if (violations.Count != 0)
        {
            Assert.Fail(string.Join(Environment.NewLine, violations));
        }
    }

    [Fact]
    public async Task DedicatedWakeupRecoversOneDurableDeliveryAfterHostRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(
            cancellationToken);
        test.ConfigureModuleParameters();
        var source = test.Neuron<IOutboxRecoverySource>("recovery-source");
        var listener = test.Neuron<IOutboxRecoveryListener>(
            "recovery-listener");
        var audit = test.Neuron<IOutboxRecoveryAudit>("recovery-audit");
        _ = listener.FailNextJournalCommit(
            "consume the deterministic outbox recovery fault");
        var committed = source.Outgoing.NextAsync<RecoveryNotice>(
            cancellationToken);

        await test.Client.SendAsync<IOutboxRecoverySource>(
            "recovery-source",
            new BeginOutboxRecovery(listener.Id, audit.Id));

        _ = await committed;
        await source.RestartHostAsync(cancellationToken);
        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(2),
            cancellationToken);

        await AssertCounts(
            source,
            listener,
            audit,
            cancellationToken);

        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(1),
            cancellationToken);

        await AssertCounts(
            source,
            listener,
            audit,
            cancellationToken);
    }

    private static async Task AssertCounts(
        TestNeuron<IOutboxRecoverySource> source,
        TestNeuron<IOutboxRecoveryListener> listener,
        TestNeuron<IOutboxRecoveryAudit> audit,
        CancellationToken cancellationToken)
    {
        Assert.Single(
            await source.Outgoing.ReadAsync<RecoveryNotice>(
                cancellationToken: cancellationToken));
        Assert.Single(
            await listener.Incoming.ReadAsync<RecoveryNotice>(
                cancellationToken: cancellationToken));
        Assert.Single(
            await listener.Outgoing.ReadAsync<RecoverySeen>(
                cancellationToken: cancellationToken));
        Assert.Single(
            await audit.Incoming.ReadAsync<RecoveryNotice>(
                cancellationToken: cancellationToken));
        Assert.Single(
            await audit.Outgoing.ReadAsync<RecoveryAudited>(
                cancellationToken: cancellationToken));
    }

    private static Type? RequireType(
        Assembly assembly,
        string name,
        List<string> violations)
    {
        var type = assembly.GetType(name);
        if (type is null)
        {
            violations.Add($"{name} does not exist.");
        }

        return type;
    }

    private static void ValidateContract(
        Type? contract,
        string alias,
        string[] methods,
        List<string> violations)
    {
        if (contract is null)
        {
            return;
        }

        if (!contract.IsInterface || contract.IsPublic)
        {
            violations.Add(
                $"{contract.FullName} must be an internal interface.");
        }

        if (!string.Equals(
                AttributeArgument(contract, "AliasAttribute"),
                alias,
                StringComparison.Ordinal))
        {
            violations.Add(
                $"{contract.FullName} must keep alias '{alias}'.");
        }

        var declared = contract
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();
        if (!declared.Select(method => method.Name).SequenceEqual(
                methods.Order(StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            violations.Add(
                $"{contract.FullName} declares [{string.Join(", ", declared.Select(method => method.Name))}], "
                + $"expected [{string.Join(", ", methods.Order(StringComparer.Ordinal))}].");
        }

        foreach (var method in declared)
        {
            if (method.Name.EndsWith("Async", StringComparison.Ordinal)
                || method.ReturnType != typeof(Task)
                || !string.Equals(
                    AttributeArgument(method, "AliasAttribute"),
                    method.Name,
                    StringComparison.Ordinal))
            {
                violations.Add(
                    $"{contract.FullName}.{method.Name} must return Task and alias its unsuffixed name.");
            }
        }
    }

    private static void ValidatePrivateReminderOwner(
        string path,
        string className,
        List<string> violations)
    {
        var source = File.ReadAllText(path);

        if (!source.Contains("IRemindable", StringComparison.Ordinal))
        {
            violations.Add(
                $"{className} must explicitly retain IRemindable.");
        }

        if (source.Contains(
                "base.ReceiveReminder",
                StringComparison.Ordinal))
        {
            violations.Add(
                $"{className} still forwards an unknown private reminder to Neuron.");
        }

        if (!source.Contains(
                "does not own reminder",
                StringComparison.Ordinal))
        {
            violations.Add(
                $"{className} must reject an unknown private reminder.");
        }
    }

    private static void ValidateNeuronWakeupComposition(
        string kernelDirectory,
        List<string> violations)
    {
        var source = string.Join(
            Environment.NewLine,
            Directory
                .EnumerateFiles(kernelDirectory, "Neuron*.cs")
                .Where(path =>
                {
                    var name = Path.GetFileName(path);
                    return string.Equals(name, "Neuron.cs", StringComparison.Ordinal)
                        || name.StartsWith("Neuron.", StringComparison.Ordinal);
                })
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
        var activation = Region(
            source,
            "public sealed override async Task OnActivateAsync",
            "public async Task Deliver");
        if (activation.Contains(
                "Wakeup().Disarm()",
                StringComparison.Ordinal))
        {
            violations.Add(
                "Empty Neuron activation still creates a helper turn by calling Wakeup().Disarm().");
        }

        var drain = Region(
            source,
            "Task IOutboxDrain.Drain()",
            "private IOutboxWakeup Wakeup()");
        var adoptsReminder = drain.IndexOf(
            "_wakeUpRegistered = true",
            StringComparison.Ordinal);
        var beginsDrain = drain.IndexOf(
            "DrainAsync(CancellationToken.None)",
            StringComparison.Ordinal);
        if (adoptsReminder < 0
            || beginsDrain < 0
            || adoptsReminder > beginsDrain)
        {
            violations.Add(
                "IOutboxDrain.Drain must mark the helper reminder as registered before draining so an orphan reminder is disarmed when the outbox is empty.");
        }
    }

    private static string Region(
        string source,
        string startMarker,
        string endMarker)
    {
        var start = source.IndexOf(
            startMarker,
            StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException(
                $"Source region start marker was not found: {startMarker}");
        }

        var end = source.IndexOf(
            endMarker,
            start,
            StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException(
                $"Source region end marker was not found after start: {endMarker}");
        }

        return source[start..end];
    }

    private static string? AttributeArgument(
        MemberInfo member,
        string attributeName)
        => member
            .GetCustomAttributesData()
            .SingleOrDefault(attribute => string.Equals(
                attribute.AttributeType.Name,
                attributeName,
                StringComparison.Ordinal))
            ?.ConstructorArguments[0]
            .Value as string;

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(
                   Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "DigitalBrain.slnx was not found above the test assembly.");
    }
}
