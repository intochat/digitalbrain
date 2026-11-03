namespace DigitalBrain.Apps.Hosting;

public sealed record PrebuiltBehavior(byte[] Assembly, byte[] Symbols, string Hash);
