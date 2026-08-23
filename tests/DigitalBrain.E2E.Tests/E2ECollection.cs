using DigitalBrain.Testing.E2E;
using Xunit;

namespace DigitalBrain.E2E.Tests;

// One shared live AppHost boot per assembly. Booting the whole distributed application
// (Azurite, silos, kernel, mcp) is expensive, so every test in this assembly shares the
// single fixture instance below.
public sealed class AppHostFixture : BrainAppHostFixture<Projects.DigitalBrain_AppHost>;

// DisableParallelization keeps this collection's tests from running concurrently against each
// other or alongside any other collection, so nothing contends over the shared AppHost boot.
// (CollectionBehaviorAttribute.DisableTestParallelization -- the assembly-level equivalent --
// is a hard compile error on this repo's xunit.v3: the obsoleted member is marked error:true.)
//
// INVARIANT: every test class in this assembly MUST join this collection -- the kernel listens
// on a fixed unproxied port (5080), so a concurrently booted second AppHost would collide.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class E2ECollection : ICollectionFixture<AppHostFixture>
{
    public const string Name = "e2e";
}
