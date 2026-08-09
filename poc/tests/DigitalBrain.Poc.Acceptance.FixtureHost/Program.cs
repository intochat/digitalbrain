using DigitalBrain.Poc.Host;

if (args is ["--normal-race", var raceStateRoot, var raceControlPlaneRoot, var token] &&
    Guid.TryParseExact(token, "N", out _))
{
    await ActiveHostBootstrap.RunNormalForTestAsync(
        Console.In,
        Console.Out,
        raceStateRoot,
        raceControlPlaneRoot,
        async (root, cancellationToken) =>
        {
            var prefix = Path.Combine(root.RootPath, "test-normal-boot-pause-" + token);
            await File.WriteAllTextAsync(prefix + ".observed", string.Empty, cancellationToken);
            while (!File.Exists(prefix + ".release"))
            {
                await Task.Delay(10, cancellationToken);
            }
        },
        CancellationToken.None);
    return 0;
}

if (args is ["--candidate-preflight", var stateRoot, var controlPlaneRoot])
{
    return await ActiveHostBootstrap.RunCandidatePreflightAsync(
        Console.In,
        Console.Out,
        stateRoot,
        controlPlaneRoot,
        CancellationToken.None);
}

if (args is ["--quarantine"])
{
    await HostScenarioProtocol.RunTrustedQuarantineAsync(
        Console.In,
        Console.Out,
        CancellationToken.None);
    return 0;
}

if (args.Length != 0)
{
    Console.Error.WriteLine("The verified-fixture host accepts no command-line module arguments.");
    return 2;
}

await HostScenarioProtocol.RunVerifiedFixtureAsync(
    Console.In,
    Console.Out,
    CancellationToken.None);
return 0;
