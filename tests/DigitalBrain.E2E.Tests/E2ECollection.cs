using DigitalBrain.Testing.E2E;
using Xunit;

namespace DigitalBrain.E2E.Tests;

// Tier 3: one shared live AppHost boot per assembly. Booting the whole distributed
// application (Azurite, silos, kernel, mcp) is expensive, so every test in this assembly
// shares the single fixture instance below.
public sealed class AppHostFixture : BrainAppHostFixture<Projects.DigitalBrain_AppHost>;

// DisableParallelization keeps this collection's tests from running concurrently against each
// other or alongside any other collection, so nothing contends over the shared AppHost boot.
// (CollectionBehaviorAttribute.DisableTestParallelization -- the assembly-level equivalent --
// is a hard compile error on this repo's xunit.v3 4.0.0-pre.154: the obsoleted member is marked
// error:true, not just warning. This collection-level property is the supported replacement,
// and since this assembly declares only this one collection, the effect is the same.)
//
// INVARIANT: every future non-BDD test class in this assembly MUST join this collection.
// The Reqnroll-generated feature classes run in the parallel phase with their own AppHost
// (BddBrainHost, feature-scoped); a classless test outside this collection would share that
// phase and collide with it on the kernel's fixed unproxied port 5080.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class E2ECollection : ICollectionFixture<AppHostFixture>
{
    public const string Name = "e2e";
}
