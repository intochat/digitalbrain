namespace DigitalBrain.Aspire.Tests;

// src/Aspire/DigitalBrain.AppHost/ProductSurfaceResources.cs declares these as `internal`
// consts, so they aren't visible from this test project; the literals are duplicated here,
// once, for every conformance test that needs them.
internal static class ProductSurfaceResourceNames
{
    public const string Kernel = "kernel";
    public const string Mcp = "mcp";
}
