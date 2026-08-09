using DigitalBrain.Poc.Host;

if (args is [var stateRoot, var controlPlaneRoot])
{
    await ActiveHostBootstrap.RunNormalAsync(
        Console.In,
        Console.Out,
        stateRoot,
        controlPlaneRoot,
        CancellationToken.None);
    return 0;
}

Console.Error.WriteLine(
    "DigitalBrain.Poc.Host requires trusted state and control-plane roots for pointer-selected boot.");
return 2;
