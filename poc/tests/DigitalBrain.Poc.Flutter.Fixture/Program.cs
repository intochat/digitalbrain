using DigitalBrain.Poc.Flutter.Fixture;

if (args.Length is not (1 or 2) ||
    (args.Length == 2 && !string.Equals(args[1], "--malformed-after-ready", StringComparison.Ordinal)))
{
    Console.Error.WriteLine("The Flutter POC fixture requires the POC root.");
    return 2;
}

return await FlutterIntegrationFixture.RunAsync(
    Console.In,
    Console.Out,
    args[0],
    args.Length == 2,
    CancellationToken.None);
