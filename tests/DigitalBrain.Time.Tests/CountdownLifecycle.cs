using System.Xml.Linq;
using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Time.Tests;

public sealed class CountdownLifecycle(TimeFixture fixture)
{
    [Fact]
    public async Task StartIsIdempotentAndAllowedOnlyFromUnscheduled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("start");
        var destination = test.Neuron<ICountdown>("destination");
        var empty = await countdown.Reference.Read();

        Assert.Equal(CountdownStatus.Unscheduled, empty.Status);
        Assert.Equal(0, empty.Generation);
        Assert.Equal(0, empty.Revision);
        Assert.Null(empty.Destination);

        var command = new StartCountdown(
            CommandId.New(),
            TimeSpan.FromHours(1),
            destination.Id);
        var started = await countdown.Reference.Start(command);
        var repeated = await countdown.Reference.Start(command);

        Assert.Equal(started, repeated);
        Assert.Equal(CountdownStatus.Scheduled, started.Status);
        Assert.Equal(1, started.Generation);
        Assert.Equal(1, started.Revision);
        Assert.Equal(destination.Id, started.Destination);
        Assert.Equal(test.Clock.UtcNow, started.ScheduledAt);
        Assert.Equal(test.Clock.UtcNow + TimeSpan.FromHours(1), started.DueAt);
        Assert.Equal(TimeSpan.FromHours(1), started.Duration);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Start(new StartCountdown(
                CommandId.New(),
                TimeSpan.FromHours(1),
                destination.Id)));
    }

    [Fact]
    public async Task RescheduleUsesTheExactRevisionAndInvalidatesThePriorWakeup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("reschedule");
        var destination = test.Neuron<ICountdown>("destination");
        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));

        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(30),
            cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Reschedule(new RescheduleCountdown(
                CommandId.New(),
                ExpectedRevision: started.Revision + 1,
                TimeSpan.FromHours(1))));

        var rescheduled = await countdown.Reference.Reschedule(
            new RescheduleCountdown(
                CommandId.New(),
                started.Revision,
                TimeSpan.FromHours(1)));

        Assert.Equal(CountdownStatus.Scheduled, rescheduled.Status);
        Assert.Equal(started.Generation, rescheduled.Generation);
        Assert.Equal(started.Revision + 1, rescheduled.Revision);
        Assert.Equal(started.Destination, rescheduled.Destination);
        Assert.Equal(test.Clock.UtcNow, rescheduled.ScheduledAt);
        Assert.Equal(test.Clock.UtcNow + TimeSpan.FromHours(1), rescheduled.DueAt);

        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(30),
            cancellationToken);
        Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(
            cancellationToken: cancellationToken));

        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(30),
            cancellationToken);
        var elapsed = await destination.Incoming.NextAsync<CountdownElapsed>(
            cancellationToken);

        Assert.Equal(rescheduled.Generation, elapsed.Synapse.Generation);
        Assert.Equal(rescheduled.Revision, elapsed.Synapse.Revision);
    }

    [Fact]
    public async Task CancelUsesTheExactRevisionAndIsTerminal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("cancel");
        var destination = test.Neuron<ICountdown>("destination");
        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Cancel(new CancelCountdown(
                CommandId.New(),
                ExpectedRevision: started.Revision + 1)));

        var command = new CancelCountdown(
            CommandId.New(),
            started.Revision);
        var cancelled = await countdown.Reference.Cancel(command);
        var repeated = await countdown.Reference.Cancel(command);

        Assert.Equal(cancelled, repeated);
        Assert.Equal(CountdownStatus.Cancelled, cancelled.Status);
        Assert.Equal(started.Generation, cancelled.Generation);
        Assert.Equal(started.Revision, cancelled.Revision);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Cancel(new CancelCountdown(
                CommandId.New(),
                cancelled.Revision)));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Reschedule(new RescheduleCountdown(
                CommandId.New(),
                cancelled.Revision,
                TimeSpan.FromHours(1))));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Start(new StartCountdown(
                CommandId.New(),
                TimeSpan.FromHours(1),
                destination.Id)));

        await test.Clock.AdvanceAsync(
            TimeSpan.FromHours(2),
            cancellationToken);
        Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(
            cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task RestartRetainsDestinationAndStartsANewGeneration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("restart");
        var destination = test.Neuron<ICountdown>("destination");
        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Restart(new RestartCountdown(
                CommandId.New(),
                TimeSpan.FromHours(1))));

        var cancelled = await countdown.Reference.Cancel(
            new CancelCountdown(CommandId.New(), started.Revision));
        var restarted = await countdown.Reference.Restart(
            new RestartCountdown(
                CommandId.New(),
                TimeSpan.FromHours(2)));

        Assert.Equal(CountdownStatus.Scheduled, restarted.Status);
        Assert.Equal(cancelled.Generation + 1, restarted.Generation);
        Assert.Equal(1, restarted.Revision);
        Assert.Equal(cancelled.Destination, restarted.Destination);
        Assert.Equal(test.Clock.UtcNow, restarted.ScheduledAt);
        Assert.Equal(test.Clock.UtcNow + TimeSpan.FromHours(2), restarted.DueAt);
        Assert.Equal(TimeSpan.FromHours(2), restarted.Duration);
    }

    [Fact]
    public async Task RestartIsAllowedAfterElapsed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("restart-elapsed");
        var destination = test.Neuron<ICountdown>("destination");
        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));

        await test.Clock.AdvanceAsync(
            TimeSpan.FromHours(1),
            cancellationToken);
        _ = await destination.Incoming.NextAsync<CountdownElapsed>(
            cancellationToken);

        var restarted = await countdown.Reference.Restart(
            new RestartCountdown(
                CommandId.New(),
                TimeSpan.FromHours(2)));

        Assert.Equal(CountdownStatus.Scheduled, restarted.Status);
        Assert.Equal(started.Generation + 1, restarted.Generation);
        Assert.Equal(1, restarted.Revision);
        Assert.Equal(started.Destination, restarted.Destination);
    }

    [Fact]
    public async Task DestinationMustBelongToTheCountdownOwner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("owner");
        var foreign = test.Owner("foreign").Neuron<ICountdown>("destination");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Start(new StartCountdown(
                CommandId.New(),
                TimeSpan.FromHours(1),
                foreign.Id)));

        Assert.Equal(
            CountdownStatus.Unscheduled,
            (await countdown.Reference.Read()).Status);
    }

    [Fact]
    public async Task CommandsRejectEmptyIdsInvalidDurationsAndDueOverflow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("validation");
        var destination = test.Neuron<ICountdown>("destination");
        var empty = default(CommandId);

        await Assert.ThrowsAsync<ArgumentException>(
            () => countdown.Reference.Start(new StartCountdown(
                empty,
                TimeSpan.FromHours(1),
                destination.Id)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => countdown.Reference.Start(new StartCountdown(
                CommandId.New(),
                TimeSpan.Zero,
                destination.Id)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => countdown.Reference.Start(new StartCountdown(
                CommandId.New(),
                TimeSpan.FromTicks(-1),
                destination.Id)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => countdown.Reference.Start(new StartCountdown(
                CommandId.New(),
                TimeSpan.MaxValue,
                destination.Id)));

        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));

        await Assert.ThrowsAsync<ArgumentException>(
            () => countdown.Reference.Reschedule(new RescheduleCountdown(
                empty,
                started.Revision,
                TimeSpan.FromHours(1))));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => countdown.Reference.Reschedule(new RescheduleCountdown(
                CommandId.New(),
                started.Revision,
                TimeSpan.Zero)));
        await Assert.ThrowsAsync<ArgumentException>(
            () => countdown.Reference.Cancel(new CancelCountdown(
                empty,
                started.Revision)));

        var cancelled = await countdown.Reference.Cancel(
            new CancelCountdown(CommandId.New(), started.Revision));

        await Assert.ThrowsAsync<ArgumentException>(
            () => countdown.Reference.Restart(new RestartCountdown(
                empty,
                TimeSpan.FromHours(1))));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => countdown.Reference.Restart(new RestartCountdown(
                CommandId.New(),
                TimeSpan.FromTicks(-1))));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => countdown.Reference.Restart(new RestartCountdown(
                CommandId.New(),
                TimeSpan.MaxValue)));

        Assert.Equal(
            cancelled,
            await countdown.Reference.Read());
    }

    [Fact]
    public async Task ReceiptsRetainOnlyTheLatestSixtyFourCommands()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("receipts");
        var destination = test.Neuron<ICountdown>("destination");
        var startCommand = new StartCountdown(
            CommandId.New(),
            TimeSpan.FromHours(1),
            destination.Id);
        var current = await countdown.Reference.Start(startCommand);
        RescheduleCountdown? oldestRetainedCommand = null;
        CountdownSnapshot? oldestRetainedSnapshot = null;

        for (var index = 0; index < 64; index++)
        {
            var command = new RescheduleCountdown(
                CommandId.New(),
                current.Revision,
                TimeSpan.FromHours(1));
            current = await countdown.Reference.Reschedule(command);

            if (index == 0)
            {
                oldestRetainedCommand = command;
                oldestRetainedSnapshot = current;
            }
        }

        Assert.Equal(
            oldestRetainedSnapshot,
            await countdown.Reference.Reschedule(
                Assert.IsType<RescheduleCountdown>(
                    oldestRetainedCommand)));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Start(startCommand));
    }

    [Fact]
    public async Task ReadReturnsCommittedStateAfterHostingSiloRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("durable-read");
        var destination = test.Neuron<ICountdown>("destination");
        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));

        await countdown.RestartHostAsync(cancellationToken);

        Assert.Equal(started, await countdown.Reference.Read());
    }

    [Fact]
    public async Task CountdownEmitsExactlyOnceAtItsDueInstant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("due");
        var destination = test.Neuron<ICountdown>("destination");
        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));

        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(59),
            cancellationToken);
        Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(
            cancellationToken: cancellationToken));

        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(1),
            cancellationToken);
        var elapsed = await destination.Incoming.NextAsync<CountdownElapsed>(
            cancellationToken);

        Assert.Equal(countdown.Id, elapsed.Synapse.Countdown);
        Assert.Equal(started.Generation, elapsed.Synapse.Generation);
        Assert.Equal(started.Revision, elapsed.Synapse.Revision);
        Assert.Equal(destination.Id, elapsed.Synapse.Destination);
        Assert.Equal(started.ScheduledAt, elapsed.Synapse.ScheduledAt);
        Assert.Equal(started.DueAt, elapsed.Synapse.DueAt);
        Assert.Equal(test.Clock.UtcNow, elapsed.Synapse.ObservedAt);
        Assert.Equal(CountdownResolution.OnTime, elapsed.Synapse.Resolution);
        Assert.Equal(
            CountdownStatus.Elapsed,
            (await countdown.Reference.Read()).Status);

        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(1),
            cancellationToken);
        Assert.Single(await destination.Incoming.ReadAsync<CountdownElapsed>(
            cancellationToken: cancellationToken));
    }

    [Fact]
    public void ExternalProjectUsesOnlyThePublicL1AuthoringSurface()
    {
        var root = LocateRepositoryRoot();
        var directory = Path.Combine(
            root,
            "tests",
            "DigitalBrain.Time.Tests");
        var project = XDocument.Load(Path.Combine(
            directory,
            "DigitalBrain.Time.Tests.csproj"));

        Assert.Equal(
            [
                "Microsoft.NET.Test.Sdk",
                "xunit.runner.visualstudio",
                "xunit.v3",
            ],
            project.Descendants("PackageReference")
                .Select(reference => (string)reference.Attribute("Include")!)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                "DigitalBrain.Modules.Time",
                "DigitalBrain.Modules.Time.Contracts",
                "DigitalBrain.Testing",
            ],
            project.Descendants("ProjectReference")
                .Select(reference => Path.GetFileNameWithoutExtension(
                    (string)reference.Attribute("Include")!))
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            "Exe",
            project.Descendants("OutputType").Single().Value);

        var forbidden = new[]
        {
            "Or" + "leans",
            "DigitalBrain.Ker" + "nel",
            "DigitalBrain." + "Client",
            "Asp" + "ire",
            "IGrain" + "Factory",
            "Grain" + "Id",
            "Get" + "Grain",
            "Task." + "Delay",
            "Thread." + "Sleep",
        };
        var violations = Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .SelectMany(path => forbidden
                .Where(token => File
                    .ReadAllText(path)
                    .Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetFileName(path)}:{token}"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void WakeupEntryPointsPreserveSerializedTurns()
    {
        var source = File.ReadAllText(Path.Combine(
            LocateRepositoryRoot(),
            "modules",
            "DigitalBrain.Modules.Time",
            "CountdownNeuron.cs"));
        var reminderStart = source.IndexOf(
            "async Task IRemindable.ReceiveReminder",
            StringComparison.Ordinal);
        var deactivationStart = source.IndexOf(
            "public override Task OnDeactivateAsync",
            reminderStart,
            StringComparison.Ordinal);
        var reminder = source[reminderStart..deactivationStart];
        var localTimerStart = source.IndexOf(
            "private void ArmLocalTimer",
            StringComparison.Ordinal);
        var disposeStart = source.IndexOf(
            "private void DisposeLocalTimer",
            localTimerStart,
            StringComparison.Ordinal);
        var localTimer = source[localTimerStart..disposeStart];

        Assert.Contains(
            "async Task ICountdownWakeup.Wake",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "=> await WakeCore(generation, revision);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "await WakeCore(generation, revision);",
            reminder,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Get" + "Grain<ICountdownWakeup>",
            reminder,
            StringComparison.Ordinal);
        Assert.Contains(
            "Get" + "Grain<ICountdownWakeup>",
            localTimer,
            StringComparison.Ordinal);
        Assert.Contains(
            "ObserveTimerWork",
            localTimer,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".GetAwaiter()",
            localTimer,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".GetResult()",
            localTimer,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RevisionAndGenerationCountersRejectOverflow()
    {
        var source = File.ReadAllText(Path.Combine(
            LocateRepositoryRoot(),
            "modules",
            "DigitalBrain.Modules.Time",
            "CountdownNeuron.cs"));

        Assert.Contains(
            "var nextRevision = checked(current.Revision + 1);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "var generation = checked(current.Generation + 1);",
            source,
            StringComparison.Ordinal);
    }

    private static Task<CountdownSnapshot> Start(
        TestNeuron<ICountdown> countdown,
        TestNeuron<ICountdown> destination,
        TimeSpan duration)
        => countdown.Reference.Start(new StartCountdown(
            CommandId.New(),
            duration,
            destination.Id));

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
