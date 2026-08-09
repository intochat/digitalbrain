# DigitalBrain POC-0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox syntax for tracking.

**Goal:** Prove that one Creator-produced C# file can become ordinary durable
neurons, safely orchestrate a trusted Flutter chart module, and survive a cold
process restart.

**Architecture:** Build a self-contained POC below poc/. It contains a fresh
runtime, a trusted Orleans-facing NeuronActivationGrain, trusted social/chart
modules, a Roslyn Creator, a restart-only host, and a minimal Flutter chart
client. A candidate is a file-based managed IL library containing two normal
neurons and one local synapse. The activation grain, envelope, immutable route
table, owner control edge, persistence, chart, and UI integration remain
trusted host code.

**Tech Stack:** .NET SDK 11.0.100-preview.6.26359.118; net11.0; Orleans
10.2.2; Microsoft.CodeAnalysis.CSharp 5.3.0; xunit.v3 4.0.0-pre.154; Flutter
with flutter_test.

## Global Constraints

- Every new source project lives under poc/. No POC project may reference
  outside that directory.
- Do not edit, extend, or import the current runtime, DigitalBrain.Scripting,
  root solution, existing clients, or the untracked Prototypes tree.
- One candidate has exactly one owner-visible C# source artifact:
  poc/candidates/<run-id>/<sha256>/elon-chart.cs. Its directory may hold
  runtime-owned metadata and IL but never a generated candidate project file.
- Trusted POC projects may have ordinary hand-authored project files. The
  one-file rule applies to the candidate only.
- Build candidates with dotnet build on the candidate source. Never publish a
  candidate. The fixed header uses OutputType=Library and PublishAot=false.
- The Creator owns the candidate header and all syntax. The candidate has no
  Main, top-level statement, ConnectAsync, static/module initializer, raw C#
  source input, or arbitrary directive.
- Candidate code sees only IDigitalBrain, IDurableState<T>, runtime time, and
  cancellation. The sole declaration-time exception is the exact
  GenerateSerializer, Alias, and member Id attribute set on generated
  contracts/state; it never sees IServiceProvider, GrainFactory, Orleans
  runtime services, activation, streams, filesystem, network, process, or UI
  APIs.
- Test/host helpers named FireTrustedAsync, DeliverAsync, or ReplayLastDeliveryAsync
  are sealed ingress/dispatcher seams, not additional candidate-visible domain
  verbs. The candidate-visible action remains IDigitalBrain.FireSynapse.
- Runtime-owned SynapseEnvelope and RouteBinding carry owner, contract alias,
  candidate family, a pinned target candidate revision for every delivery into
  generated code, producing revision for candidate-local traffic, target scope,
  capability origin, causation, and delivery identity. They are never
  candidate-visible arguments or source constructs.
- A trusted NeuronActivationGrain invokes normal generated Neuron handlers. It
  is shared runtime machinery, not a ScriptedNeuron or behavior interpreter.
- Each owner rule has a host-generated stable candidate-family ID. One active
  revision is allowed per family; type, assembly, alias, and route identity are
  family-qualified so different owners can coexist.
- SocialPostObserved, AddChartPoint, ChartPointAdded, ChartNeuron, and the
  Flutter bridge are trusted module code, never generated code.
- Owner identity is assigned by trusted ingress/control-plane authentication,
  never source text, arguments, or a synapse field.
- Creator policy is not an OS sandbox for hostile arbitrary IL. POC-0 accepts
  only hash-verified Creator output and must not advertise hostile-code
  containment.
- Every POC owner-data root is disposable. A teardown proof must remove its
  journal, outbox, snapshots, chart projection, test sessions, candidate
  evidence, and control-plane test records; production retention/deletion is
  explicitly out of scope until it has its own design.
- Every test that materializes a candidate, process, journal, session, or
  control-plane record owns a fresh async-disposable run lease. It uses an
  await-using declaration directly or a per-test IAsyncLifetime fixture whose
  DisposeAsync performs the same parent-store residual scan; no test-class or
  shared fixture may own mutable POC data across facts.
- Every code task starts with its listed failing test and ends with its focused
  test run. Before execution, make an isolated worktree. This document phase
  makes no commit; a later code commit requires separate owner authorization.

---

## Target file structure

~~~text
poc/
  .gitignore
  global.json
  Directory.Build.props
  Directory.Packages.props
  DigitalBrain.Poc.slnx
  src/
    DigitalBrain.Poc.Abstractions/
    DigitalBrain.Poc.Runtime/
    DigitalBrain.Poc.Social.Contracts/
    DigitalBrain.Poc.Charting.Contracts/
    DigitalBrain.Poc.Charting/
    DigitalBrain.Poc.Creator/
    DigitalBrain.Poc.ControlPlane/
    DigitalBrain.Poc.Host/
  tests/
    DigitalBrain.Poc.Foundation.Tests/
    DigitalBrain.Poc.Runtime.Tests/
    DigitalBrain.Poc.Creator.Tests/
    DigitalBrain.Poc.Acceptance.Tests/
  flutter/chart_poc/
  candidates/<run-id>/<sha256>/
    elon-chart.cs
    candidate.json
    module.dll
  control-plane-store/<run-id>/
    attestations/
    approvals/
    active-pointers/
~~~

Candidate directories are ignored runtime data. Source control never contains
a generated candidate. Each PocDataRoot owns one unique run-id, so a
content-identical candidate is not shared across runs. The control-plane store
is also ignored runtime data, but it is deliberately outside every candidate
directory and is owned only by the trusted host/test authority.

The one POC route is:

~~~text
trusted SocialPostObserved
  → generated ElonPostRuleNeuron
  → generated ElonPostMatched
  → generated ChartForwarderNeuron
  → trusted AddChartPoint
  → trusted ChartNeuron
  → trusted Flutter chart projection
~~~

The existing Flutter tree has static chart demos only. It has no ChartNeuron,
AddChartPoint, or ChartPoint module to reuse. Task 4 creates a fresh trusted
chart vertical slice before the Creator exists.

## Shared interfaces

Task 2 establishes this public POC surface and later tasks use these exact
names:

~~~csharp
namespace DigitalBrain.Poc.Abstractions;

public abstract record Synapse;

public interface IHandle<TSynapse>
    where TSynapse : Synapse
{
    Task HandleAsync(TSynapse synapse, CancellationToken cancellationToken);
}

public interface IDigitalBrain
{
    Task FireSynapse(
        Synapse synapse,
        CancellationToken cancellationToken = default);
}

public interface IDurableState<TState>
{
    TState Value { get; }
    void Replace(TState next);
}
~~~

Task 4 establishes these host-owned integration contracts:

~~~csharp
public sealed record SocialPostObserved(
    string PostId,
    string Author,
    DateTimeOffset OccurredAt) : Synapse;

public sealed record ChartPoint(
    string SourcePostId,
    DateTimeOffset OccurredAt,
    int Ordinal);

public sealed record ChartPointDraft(
    string SourcePostId,
    DateTimeOffset OccurredAt);

public sealed record AddChartPoint(
    string ChartId,
    ChartPointDraft Draft) : Synapse;

public sealed record ChartPointAdded(
    string ChartId,
    ChartPoint Point,
    string EffectId) : Synapse;
~~~

Each concrete record above is emitted with Orleans GenerateSerializer and
host-owned Alias attributes. The fixed POC aliases are
db.poc.social.post-observed.v1, db.poc.chart.point.v1,
db.poc.chart.point-draft.v1, db.poc.chart.add-point.v1, and
db.poc.chart.point-added.v1.

Task 5’s Creator emits exactly this local module vocabulary:

~~~csharp
public sealed record ElonPostMatched(
    string PostId,
    DateTimeOffset OccurredAt,
    int RuleOrdinal) : Synapse;

public sealed record ElonPostRuleState(
    int AcceptedCount);

public sealed class ElonPostRuleNeuron :
    Neuron,
    IHandle<SocialPostObserved>;

public sealed class ChartForwarderNeuron :
    Neuron,
    IHandle<ElonPostMatched>;
~~~

The Creator emits serializer declarations and host-derived,
family-qualified aliases. For family cf_aaaaaaaaaaaaaaaaaaaaaaaaaa they are
db.poc.family.cf_aaaaaaaaaaaaaaaaaaaaaaaaaa.matched.v1 and
db.poc.family.cf_aaaaaaaaaaaaaaaaaaaaaaaaaa.state.v1. They remain stable for
behavior-only revisions of that family; a schema change is incompatible and
POC-0 refuses it. A different owner family receives a different prefix, so
two accepted modules cannot collide at the serializer registry.

## Task 1: Establish a genuinely isolated POC solution

**Files:**

- Create: poc/.gitignore
- Create: poc/global.json
- Create: poc/Directory.Build.props
- Create: poc/Directory.Packages.props
- Create: poc/DigitalBrain.Poc.slnx
- Create: all source project files in the target file structure
- Create: poc/tests/DigitalBrain.Poc.Foundation.Tests/PocBoundaryFacts.cs

**Produces:** a separately buildable solution with a machine-enforced
no-legacy-reference boundary.

- [ ] **Step 1: Write the failing boundary test**

~~~csharp
[Fact]
public void EveryProjectReferenceStaysInsidePoc()
{
    var references = ProjectReferenceScanner.ReadAll(PocPaths.Root);

    Assert.All(
        references,
        path => Assert.StartsWith(
            PocPaths.Root,
            Path.GetFullPath(path),
            StringComparison.OrdinalIgnoreCase));
}
~~~

The scanner enumerates every local path-bearing MSBuild item in poc/**/*.csproj
and props/targets files: ProjectReference, Compile/Content/None with Link,
Import, Analyzer, and HintPath. It normalizes each local path and rejects a
path to DigitalBrain.Scripting, src/, clients/, or Prototypes/. SDK and NuGet
reference roots are allowed only when they come from the SDK/package resolver,
not from a hand-authored local path.

- [ ] **Step 2: Run the test to confirm it is red**

Run: dotnet test poc/tests/DigitalBrain.Poc.Foundation.Tests -c Release

Expected: FAIL because the POC solution and scanner do not exist.

- [ ] **Step 3: Create local pins and project graph**

Create a POC-local global.json pinned to:

~~~json
{
  "sdk": {
    "version": "11.0.100-preview.6.26359.118",
    "rollForward": "latestFeature",
    "allowPrerelease": true
  }
}
~~~

Create a POC-local central package file with these pins:

~~~xml
<PackageVersion Include="Microsoft.Orleans.Server" Version="10.2.2" />
<PackageVersion Include="Microsoft.Orleans.Sdk" Version="10.2.2" />
<PackageVersion Include="Microsoft.Orleans.Client" Version="10.2.2" />
<PackageVersion Include="Microsoft.Orleans.TestingHost" Version="10.2.2" />
<PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="5.3.0" />
<PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
<PackageVersion Include="xunit.v3" Version="4.0.0-pre.154" />
~~~

Set net11.0, nullable enabled, implicit usings disabled, and warnings-as-errors
in local build props. Ignore candidates/, control-plane-store/, artifacts/,
bin/, and obj/ but do not ignore all project files.

- [ ] **Step 4: Implement scanner and make the boundary green**

Add one assertion that candidates are ignored while project files are not:

~~~csharp
[Fact]
public void RuntimeCandidateDataIsIgnored()
{
    var ignore = File.ReadAllText(Path.Combine(PocPaths.Root, ".gitignore"));

    Assert.Contains("candidates/", ignore, StringComparison.Ordinal);
    Assert.Contains("control-plane-store/", ignore, StringComparison.Ordinal);
    Assert.DoesNotContain("*.csproj", ignore, StringComparison.Ordinal);
}
~~~

- [ ] **Step 5: Run focused and solution gates**

Run: dotnet test poc/tests/DigitalBrain.Poc.Foundation.Tests -c Release

Expected: PASS.

Run: dotnet build poc/DigitalBrain.Poc.slnx -c Release

Expected: 0 errors and no project reference outside poc/.

## Task 2: Prove the one-file normal-neuron feasibility spike

**Files:**

- Create: poc/src/DigitalBrain.Poc.Abstractions/Synapse.cs
- Create: poc/src/DigitalBrain.Poc.Abstractions/IHandle.cs
- Create: poc/src/DigitalBrain.Poc.Abstractions/IDigitalBrain.cs
- Create: poc/src/DigitalBrain.Poc.Abstractions/IDurableState.cs
- Create: poc/src/DigitalBrain.Poc.Abstractions/Neuron.cs
- Create: poc/src/DigitalBrain.Poc.Abstractions/ProbeIngress.cs
- Create: poc/tests/DigitalBrain.Poc.Foundation.Tests/FileModuleBuildFacts.cs
- Create: poc/tests/DigitalBrain.Poc.Foundation.Tests/FileModuleBuilder.cs
- Create: poc/tests/DigitalBrain.Poc.Foundation.Tests/CandidateTestRun.cs
- Create: poc/tests/DigitalBrain.Poc.Foundation.Tests/Fixtures/probe-neuron.cs

**Consumes:** Task 1’s isolated build.

**Produces:** proof that a C# file with no candidate project file builds as
managed IL with the candidate-defined serializer. Runtime delivery begins only
after Task 3 provides the real activation/routing path.

- [ ] **Step 1: Write three failing feasibility tests**

~~~csharp
[Fact]
public async Task SingleFileLibraryBuildsWithoutCandidateProject()
{
    await using var run = CandidateTestRun.Create();
    var result = await FileModuleBuilder.BuildAsync(run, FixturePaths.ProbeNeuron);

    Assert.True(result.Succeeded, result.Diagnostics);
    Assert.Single(Directory.EnumerateFiles(
        result.CandidateDirectory,
        "*.cs",
        SearchOption.AllDirectories));
    Assert.Empty(Directory.EnumerateFiles(
        result.CandidateDirectory,
        "*.csproj",
        SearchOption.AllDirectories));
}

[Fact]
public async Task CandidateOutputIsManagedIl()
{
    await using var run = CandidateTestRun.Create();
    var result = await FileModuleBuilder.BuildAsync(run, FixturePaths.ProbeNeuron);

    using var stream = File.OpenRead(result.AssemblyPath);
    using var reader = new PEReader(stream);
    Assert.True(reader.HasMetadata);
}

[Fact]
public async Task CandidateUsesOnlyTheFixedHeaderAndOneSourceFile()
{
    await using var run = CandidateTestRun.Create();
    var candidate = await FileModuleBuilder.BuildAsync(run, FixturePaths.ProbeNeuron);

    Assert.True(candidate.FixedHeaderVerified);
    Assert.Single(candidate.DeclaredTypes.Where(type => type.Name == "ProbeNeuron"));
    Assert.Single(candidate.DeclaredTypes.Where(type => type.Name == "ProbeSynapse"));
}

[Fact]
public async Task CandidateDefinedSynapseHasTheExpectedGeneratedSerializerAlias()
{
    await using var run = CandidateTestRun.Create();
    var candidate = await FileModuleBuilder.BuildAsync(run, FixturePaths.ProbeNeuron);

    Assert.Contains(
        candidate.ContractAliases,
        alias => alias == "db.poc.family.cf_cccccccccccccccccccccccccc.matched.v1");
}
~~~

- [ ] **Step 2: Run tests to confirm they are red**

Run: dotnet test poc/tests/DigitalBrain.Poc.Foundation.Tests -c Release

Expected: FAIL because no ABI, builder, or candidate exists.

- [ ] **Step 3: Add the minimal ABI and fixed probe header**

Implement the Shared interfaces. Neuron is a plain public compiled base with
only protected scoped IDigitalBrain and typed durable-state access; the trusted
Orleans-facing activation adapter is deliberately deferred to Task 3.

The probe starts with:

~~~csharp
#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net11.0
#:property OutputType=Library
#:property PublishAot=false
#:property ImplicitUsings=disable
#:property AssemblyName=DigitalBrain.Poc.Candidate.cf_cccccccccccccccccccccccccc
#:project ../../../src/DigitalBrain.Poc.Abstractions/DigitalBrain.Poc.Abstractions.csproj
~~~

Its body declares ProbeSynapse, ProbeEmitterNeuron : Neuron,
IHandle<ProbeIngress>, and ProbeNeuron : Neuron, IHandle<ProbeSynapse>. Its
types live in the family-qualified
DigitalBrain.Poc.Candidate.cf_cccccccccccccccccccccccccc namespace.
ProbeIngress is a trusted static fixture contract. ProbeSynapse has Orleans
GenerateSerializer and Alias declarations, and every serialized member has a
host-generated contiguous Id beginning at zero. Its unique alias is
db.poc.family.cf_cccccccccccccccccccccccccc.matched.v1. The file has no Main
or top-level statement.

- [ ] **Step 4: Implement canonical file build and directive mutation checks**

CandidateTestRun allocates a unique run ID, candidate root, control-plane root,
and build scratch for every feasibility fact. Its async disposal removes only
those resolved paths and fails the test if a parent-store scan still finds the
run ID. FileModuleBuilder receives that run, reads the proposed bytes into
memory, verifies the exact fixed header and one-file rule, and computes their
canonical hash before it creates any candidate directory. Only then does it
copy that verified source to its canonical ignored candidate directory at
poc/candidates/<run-id>/<sha256>/probe-neuron.cs, invoke dotnet build against the copied
source, capture stdout/stderr, and return the verified staged assembly. This
makes the fixed ../../../src project references resolve from the same depth as
the final candidate.

Pass build-specific BaseIntermediateOutputPath and BaseOutputPath values under
the run’s temporary build scratch outside candidates/. Copy only the verified
managed module.dll and canonical candidate.json evidence back to the candidate
directory. The recursive one-C# assertion therefore proves that no obj/ or
other generated source was retained beside the one owner-visible file.

Add red mutations for #:include, changed #:sdk, changed #:project, changed
#:property, and #:package. Each mutation must fail before build and must not
leave a candidate directory.

- [ ] **Step 5: Run feasibility gates**

Run: dotnet test poc/tests/DigitalBrain.Poc.Foundation.Tests -c Release

Expected: PASS. Verify one source, zero candidate project files, managed IL,
fixed directives, and the generated ProbeSynapse serializer alias.

**Hard stop:** If this fails, do not introduce a generated project, interpreter,
or hot loading. Resolve the one-file build/source-generator question first.

## Task 3: Build the fresh durable core and process-restart harness

**Files:**

- Create: poc/src/DigitalBrain.Poc.Runtime/DurableTurn.cs
- Create: poc/src/DigitalBrain.Poc.Runtime/JournalStore.cs
- Create: poc/src/DigitalBrain.Poc.Runtime/Outbox.cs
- Create: poc/src/DigitalBrain.Poc.Runtime/ExactHandlerCatalog.cs
- Create: poc/src/DigitalBrain.Poc.Runtime/DurableState.cs
- Create: poc/src/DigitalBrain.Poc.Runtime/PocDataRoot.cs
- Create: poc/src/DigitalBrain.Poc.Runtime/BrainFacade.cs
- Create: poc/src/DigitalBrain.Poc.Runtime/CandidateFamilyId.cs
- Create: poc/src/DigitalBrain.Poc.ControlPlane/CandidateFamilyMinter.cs
- Create: poc/src/DigitalBrain.Poc.Runtime/SynapseEnvelope.cs
- Create: poc/src/DigitalBrain.Poc.Runtime/RouteBinding.cs
- Create: poc/src/DigitalBrain.Poc.Runtime/ImmutableRouteTable.cs
- Create: poc/src/DigitalBrain.Poc.Runtime/NeuronActivationGrain.cs
- Create: poc/src/DigitalBrain.Poc.Runtime/CandidateAssemblyLoader.cs
- Create: poc/src/DigitalBrain.Poc.Host/Program.cs
- Create: poc/src/DigitalBrain.Poc.Host/HostScenarioProtocol.cs
- Create: poc/src/DigitalBrain.Poc.Host/TestOwnerAuthority.cs
- Create: poc/src/DigitalBrain.Poc.Host/TestFaults.cs
- Create: poc/tests/DigitalBrain.Poc.Runtime.Tests/DurableTurnFacts.cs
- Create: poc/tests/DigitalBrain.Poc.Runtime.Tests/CapabilityEnforcementFacts.cs
- Create: poc/tests/DigitalBrain.Poc.Runtime.Tests/CandidateFamilyIdFacts.cs
- Create: poc/tests/DigitalBrain.Poc.Runtime.Tests/PocDataRootFacts.cs
- Create: poc/tests/DigitalBrain.Poc.Runtime.Tests/CandidateDeliveryFacts.cs
- Create: poc/tests/DigitalBrain.Poc.Runtime.Tests/DurableProbeNeuron.cs
- Create: poc/tests/DigitalBrain.Poc.Runtime.Tests/IncrementAndEmit.cs
- Create: poc/tests/DigitalBrain.Poc.Runtime.Tests/ThrowAfterStateAndEmit.cs
- Create: poc/tests/DigitalBrain.Poc.Runtime.Tests/ReplaceProbeState.cs
- Create: poc/tests/DigitalBrain.Poc.Acceptance.Tests/HostProcess.cs
- Create: poc/tests/DigitalBrain.Poc.Acceptance.Tests/ColdRestartFacts.cs

**Consumes:** Task 2 ABI and verified candidate build.

**Produces:** exact route bindings, scoped envelopes, trusted activation,
journal, typed state, outbox, deduplication, and evidence from two distinct
operating-system processes.

- [ ] **Step 1: Write failing atomic-turn and restart tests**

~~~csharp
[Fact]
public async Task StateAndOutgoingSynapseCommitOrRollbackTogether()
{
    var probe = new DurableProbeNeuron();

    await probe.HandleAsync(new IncrementAndEmit(), CancellationToken.None);

    Assert.Equal(1, await probe.ReadCountAsync());
    Assert.Equal(
        ["IncrementAndEmit", "Emitted"],
        await probe.ReadJournalKindsAsync());
}

[Fact]
public async Task NewHostProcessRestoresStateAndCommittedOutbox()
{
    await using var stateRoot = TemporaryStateRoot.Create();
    await using var first = await HostProcess.StartAsync(stateRoot);
    await first.FireTrustedAsync(
        _owners.SessionFor("owner-a"),
        new IncrementAndEmit());
    var firstPid = first.ProcessId;

    await first.TerminateAsync();

    await using var second = await HostProcess.StartAsync(stateRoot);
    var snapshot = await second.ReadSnapshotAsync();

    Assert.NotEqual(firstPid, second.ProcessId);
    Assert.Equal(1, snapshot.AcceptedCount);
    Assert.Contains("Emitted", snapshot.JournalKinds);
}

[Fact]
public async Task InvocationCannotFireAnUngrantedContract()
{
    var invocation = _brain.ForCandidate(
        ownerScope: _owners.CandidateScopeForTest("owner-a"),
        outputs: [typeof(AllowedOutput)]);

    await Assert.ThrowsAsync<CapabilityDeniedException>(
        () => invocation.FireSynapse(new ForbiddenOutput()));
}

private sealed record AllowedOutput : Synapse;
private sealed record ForbiddenOutput : Synapse;

[Fact]
public void CandidateFamilyIdIsOpaqueIdentifierSafeAndRejectsDisplayNames()
{
    var minted = _familyMinter.Mint();

    Assert.Matches("^cf_[a-z2-7]{26}$", minted.Value);
    Assert.Throws<FormatException>(
        () => CandidateFamilyId.Parse("owner-a.elon-chart"));
}

[Fact]
public async Task MinterRetriesACollisionBeforeItReservesAFamily()
{
    var existing = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
    await _families.ReserveAsync(existing);
    var minter = new CandidateFamilyMinter(
        new SequenceBase32Source(
            "aaaaaaaaaaaaaaaaaaaaaaaaaa",
            "bbbbbbbbbbbbbbbbbbbbbbbbbb"),
        _families);

    var minted = await minter.MintAndReserveAsync();

    Assert.Equal("cf_bbbbbbbbbbbbbbbbbbbbbbbbbb", minted.Value);
}

[Fact]
public async Task ThrowAfterStateAndEmitLeavesNoAcknowledgedTurnOrOutboxWork()
{
    await Assert.ThrowsAsync<ProbeFailureException>(
        () => _brain.FireTrustedAsync(
            _owners.SessionFor("owner-a"),
            new ThrowAfterStateAndEmit()));

    Assert.Equal(0, await _probes.ReadCountAsync());
    Assert.Empty(await _outbox.ReadPendingAsync());
    Assert.False(await _journal.HasAcknowledgedReceiptAsync("throwing-input-1"));
}

[Fact]
public async Task StateOverTheConfiguredByteLimitRollsBackTheWholeTurn()
{
    await Assert.ThrowsAsync<StateTooLargeException>(
        () => _brain.FireTrustedAsync(
            _owners.SessionFor("owner-a"),
            new ReplaceProbeState(new string('x', 65_537))));

    Assert.Equal(0, await _probes.ReadCountAsync());
    Assert.Empty(await _outbox.ReadPendingAsync());
    Assert.False(await _journal.HasAcknowledgedReceiptAsync("oversized-input-1"));
}

[Fact]
public async Task DisposingAPocRunErasesAllOwnerDataAndTestEvidence()
{
    var run = PocDataRoot.Create();
    var ownerSession = _owners.SessionFor("owner-a");
    await using (var host = await HostProcess.StartAsync(run))
    {
        await host.FireTrustedAsync(ownerSession, new IncrementAndEmit());
    }

    await run.DisposeAsync();

    Assert.Empty(await PocDataRoot.FindArtifactsForRunAsync(
        PocPaths.Root,
        run.RunId));
    Assert.False(Directory.Exists(run.RootPath));
}

[Fact]
public async Task VerifiedCandidateLocalSynapseCrossesActivationAndRuntimeBoundary()
{
    await using var stateRoot = TemporaryStateRoot.Create();
    var candidate = await _fixtures.BuildProbeCandidateAsync(
        stateRoot,
        CandidateFamilyId.Parse("cf_cccccccccccccccccccccccccc"));
    await using var host = await HostProcess.StartVerifiedFixtureAsync(
        stateRoot,
        candidate);

    await host.FireTrustedAsync(
        _owners.SessionFor("owner-a"),
        new ProbeIngress("probe-1"));

    Assert.Equal(1, await host.ReadHandledCountAsync(
        candidate.Manifest.Contract(
            "db.poc.family.cf_cccccccccccccccccccccccccc.matched.v1")));
    Assert.Equal(
        ["ProbeIngress", "ProbeSynapse"],
        await host.JournalKindsAsync());
}

[Fact]
public async Task CandidateProbeSynapseRoundTripsThroughConfiguredOrleansSerializer()
{
    await using var stateRoot = TemporaryStateRoot.Create();
    var candidate = await _fixtures.BuildProbeCandidateAsync(
        stateRoot,
        CandidateFamilyId.Parse("cf_cccccccccccccccccccccccccc"));
    await using var host = await HostProcess.StartVerifiedFixtureAsync(
        stateRoot,
        candidate);

    var roundTrip = await host.RoundTripCandidateSynapseAsync(
        candidate.Manifest.Contract(
            "db.poc.family.cf_cccccccccccccccccccccccccc.matched.v1"),
        new ProbeIngress("probe-serializer"));

    Assert.Equal("probe-serializer", roundTrip.ProbeId);
    Assert.Equal(
        "db.poc.family.cf_cccccccccccccccccccccccccc.matched.v1",
        roundTrip.ContractAlias);
}

[Fact]
public async Task OwnerBCannotInvokeOwnerACandidateRoute()
{
    await using var stateRoot = TemporaryStateRoot.Create();
    var candidate = await _fixtures.BuildProbeCandidateAsync(
        stateRoot,
        CandidateFamilyId.Parse("cf_cccccccccccccccccccccccccc"));
    await using var host = await HostProcess.StartVerifiedFixtureAsync(
        stateRoot,
        candidate);

    await Assert.ThrowsAsync<AuthorizationException>(
        () => host.FireTrustedAsync(
            _owners.SessionFor("owner-b"),
            new ProbeIngress("probe-1")));
}

[Fact]
public async Task OwnerInputFansOutOnceToEachGrantedCandidateFamily()
{
    await using var stateRoot = TemporaryStateRoot.Create();
    var ownerSession = _owners.SessionFor("owner-a");
    var first = await _fixtures.BuildProbeCandidateAsync(
        stateRoot,
        CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa"));
    var second = await _fixtures.BuildProbeCandidateAsync(
        stateRoot,
        CandidateFamilyId.Parse("cf_bbbbbbbbbbbbbbbbbbbbbbbbbb"));
    var excluded = await _fixtures.BuildOtherTriggerCandidateAsync(
        stateRoot,
        CandidateFamilyId.Parse("cf_cccccccccccccccccccccccccc"));
    await using var host = await HostProcess.StartVerifiedFixtureAsync(
        stateRoot,
        first,
        second,
        excluded);

    await host.FireTrustedAsync(ownerSession, new ProbeIngress("fanout-1"));
    await host.FireTrustedAsync(ownerSession, new ProbeIngress("fanout-1"));

    Assert.Equal(
        1,
        await host.ReadHandledCountAsync(
            first.Manifest.Contract(
                "db.poc.family.cf_aaaaaaaaaaaaaaaaaaaaaaaaaa.matched.v1")));
    Assert.Equal(
        1,
        await host.ReadHandledCountAsync(
            second.Manifest.Contract(
                "db.poc.family.cf_bbbbbbbbbbbbbbbbbbbbbbbbbb.matched.v1")));
    Assert.Equal(0, await host.ReadTurnCountAsync(excluded.Family));
    Assert.NotEqual(first.LocalSynapseAlias, second.LocalSynapseAlias);
}
~~~

RoundTripCandidateSynapseAsync is a host-test seam over the configured Orleans
serializer: it obtains the manifest-bound candidate runtime type, serializes
the actual ProbeSynapse bytes, deserializes them through that same configured
serializer, and returns a typed test view. It must not use JSON, reflection
copying, or an in-memory equality shortcut.

- [ ] **Step 2: Run tests to confirm they are red**

Run: dotnet test poc/tests/DigitalBrain.Poc.Runtime.Tests -c Release

Expected: FAIL because no durable runtime exists.

Run: dotnet test poc/tests/DigitalBrain.Poc.Acceptance.Tests -c Release

Expected: FAIL because no independent host process exists.

- [ ] **Step 3: Implement the sealed durable-turn boundary**

Persist inbound receipt, deduplication watermark, typed state replacement, and
ordered outgoing envelopes in one durable transaction. A handler exception
rolls all four back; it may record an error diagnostic but never an acknowledged
receipt or deliverable outbox entry. Only a committed turn may enter the
outbox. The runtime derives delivery/effect ID from durable input plus output
ordinal; candidate code cannot create it. Reject a state replacement whose
serialized byte count exceeds the policy limit.

PocDataRoot allocates a unique test-run identity and records every journal,
outbox, snapshot, chart projection, test-session, candidate-evidence, and
control-plane path created for that run. DisposeAsync removes only those
resolved paths. Candidate and control-plane data live under
candidates/<run-id>/ and control-plane-store/<run-id>/, so a content-identical
candidate is never shared with another run. The deletion test searches those
parent stores by run ID after disposal rather than merely observing that the
run root is gone. It is POC test hygiene, not a substitute for the later
product retention/deletion design.

SynapseEnvelope is created only by trusted ingress or a scoped brain proxy.
For a trusted input, ImmutableRouteTable expands exactly one output envelope
for every active family under that owner whose grant contains the trigger
contract; each envelope captures that family’s current target revision and a
distinct delivery identity, and no envelope is created for an ungranted
family. A candidate-local envelope records both producing and target revision;
in POC-0 they must be the same immutable revision. Dispatch resolves the
pinned target revision, never a later active revision. RouteBinding keys each
activation by owner, contract, candidate family/revision when present, target
scope when present, and neuron type. The activation grain loads the verified
candidate assembly before host startup, constructs the selected normal Neuron
with only scoped IDigitalBrain and IDurableState<T>, and invokes its exact
IHandle<T>.
ExactHandlerCatalog rejects a Synapse base handler and unknown alias before a
turn begins.

CandidateFamilyMinter, not a caller or display name, produces a collision-free
CandidateFamilyId in the exact cf_ + 26 lowercase-base32 form and reserves it
in the trusted catalog before Creator work begins. Assembly name, namespace,
and local aliases derive from that value only.

Task 3 uses test-only VerifiedCandidateModule instances made directly from
Task 2's fixed-header/source/IL evidence. Its fixture can load a finite
explicit list only to prove startup activation, configured serialization, and
same-owner per-family fan-out; those modules are never approvable. The excluded
fixture family handles only OtherProbeIngress and holds no ProbeIngress grant,
so its zero-turn assertion proves grant filtering rather than a missing type.
Task 6 replaces that test fixture with the signed control-plane attestation
required by real quarantine, promotion, and boot.
HostProcess.StartVerifiedFixtureAsync is the only direct module-injection
helper; it exists only in test assemblies and has no corresponding normal
Program command-line mode.

- [ ] **Step 4: Implement a real process oracle**

The test-only fixture bootstrap supplies a POC state-root path, a finite list
of already-verified fixture modules, and a trusted local test-scenario protocol
at host construction. It is not a normal Program command-line mode and cannot
be selected outside the test assembly. TestOwnerAuthority issues opaque
sessions to HostProcess. HostProcess starts that fixture executable with
ProcessStartInfo, captures its PID, fires/reads through authenticated sessions,
and terminates the whole process. Do not substitute TestCluster.RestartSiloAsync
for this proof.

- [ ] **Step 5: Run durable-core gates**

Run: dotnet test poc/tests/DigitalBrain.Poc.Runtime.Tests -c Release

Expected: PASS.

Run: dotnet test poc/tests/DigitalBrain.Poc.Acceptance.Tests -c Release

Expected: PASS with different PIDs, restored state/outbox, rollback on handler
failure, state-size refusal, owner-isolated routes, same-owner per-family
fan-out with deduplication, real candidate-local synapse delivery, actual
configured-Orleans serializer roundtrip, and deletion of every registered POC
test artifact.

## Task 4: Build the trusted chart vertical slice first

**Files:**

- Create: poc/src/DigitalBrain.Poc.Social.Contracts/SocialPostObserved.cs
- Create: poc/src/DigitalBrain.Poc.Charting.Contracts/ChartPoint.cs
- Create: poc/src/DigitalBrain.Poc.Charting.Contracts/ChartPointDraft.cs
- Create: poc/src/DigitalBrain.Poc.Charting.Contracts/AddChartPoint.cs
- Create: poc/src/DigitalBrain.Poc.Charting.Contracts/ChartPointAdded.cs
- Create: poc/src/DigitalBrain.Poc.Charting/ChartNeuron.cs
- Create: poc/src/DigitalBrain.Poc.Charting/ChartProjectionEndpoint.cs
- Create: poc/src/DigitalBrain.Poc.Host/ChartProjectionRoutes.cs
- Create: poc/tests/DigitalBrain.Poc.Runtime.Tests/ChartNeuronFacts.cs
- Create: poc/flutter/chart_poc/lib/chart_projection.dart
- Create: poc/flutter/chart_poc/lib/chart_projection_http_client.dart
- Create: poc/flutter/chart_poc/lib/chart_plot.dart
- Create: poc/flutter/chart_poc/lib/chart_screen.dart
- Create: poc/flutter/chart_poc/lib/main.dart
- Create: poc/flutter/chart_poc/pubspec.yaml
- Create: poc/flutter/chart_poc/windows/ (Flutter-generated Windows runner)
- Create: poc/flutter/chart_poc/test/chart_screen_test.dart

**Consumes:** Task 3 durable core.

**Produces:** host-owned social/chart contracts, a durable trusted ChartNeuron,
and a minimal Flutter chart. This module is never generated.

- [ ] **Step 1: Write failing chart and Flutter tests**

~~~csharp
[Fact]
public async Task TrustedChartNeuronPersistsOnePointAndPublishesOneFact()
{
    await _brain.FireAsync(
        _owners.SessionFor("owner-a"),
        new AddChartPoint(
            "elon-chart",
            new ChartPointDraft("post-1", _clock.UtcNow)));

    var snapshot = await _charts.ReadAsync(
        _owners.SessionFor("owner-a"),
        "elon-chart");

    Assert.Equal([1], snapshot.Points.Select(point => point.Ordinal));
    Assert.Single(await _journal.FindAsync<ChartPointAdded>());
}

[Fact]
public async Task ReplayedEffectIdDoesNotDuplicateAChartPoint()
{
    await _brain.DeliverAsync(EffectIds.For("input-1", 0));
    await _brain.DeliverAsync(EffectIds.For("input-1", 0));

    Assert.Single((await _charts.ReadAsync(
        _owners.SessionFor("owner-a"),
        "elon-chart")).Points);
}

[Fact]
public async Task ForeignOwnerOrUngrantTargetCannotMutateAChart()
{
    var invocation = _brain.ForCandidate(
        ownerScope: _owners.CandidateScopeForTest("owner-b"),
        outputs: [typeof(AddChartPoint)],
        charts: ["owner-b-chart"]);

    await Assert.ThrowsAsync<CapabilityDeniedException>(
        () => invocation.FireSynapse(
            new AddChartPoint(
                "owner-a-chart",
                new ChartPointDraft("post-1", _clock.UtcNow))));

    Assert.Empty((await _charts.ReadAsync(
        _owners.SessionFor("owner-a"),
        "owner-a-chart")).Points);
}

[Fact]
public async Task ProjectionEndpointRejectsForgedOwnerToken()
{
    var response = await _projection.GetAsync(
        bearerToken: "owner-a",
        chartId: "elon-chart");

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Fact]
public async Task ProjectionEndpointHidesAnotherOwnersChart()
{
    var response = await _projection.GetAsync(
        bearerToken: _owners.SessionFor("owner-b").OpaqueToken,
        chartId: "owner-a-chart");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}
~~~

~~~dart
testWidgets('renders the persisted chart point', (tester) async {
  final projection = FakeChartProjection(
    const [ChartPointView(sourcePostId: 'post-1', ordinal: 1)],
  );

  await tester.pumpWidget(ChartScreen(projection: projection));
  await tester.pump();

  expect(find.byType(CustomPaint), findsOneWidget);
  expect(find.text('1'), findsOneWidget);
});
~~~

- [ ] **Step 2: Run tests to confirm they are red**

Run: dotnet test poc/tests/DigitalBrain.Poc.Runtime.Tests -c Release

Expected: FAIL because contracts and ChartNeuron do not exist.

Run: cd poc/flutter/chart_poc; flutter test

Expected: FAIL because the fresh Flutter app does not exist.

- [ ] **Step 3: Implement trusted chart handling**

ChartNeuron is the only valid AddChartPoint handler for a chart target. It
validates runtime owner and capability grant, deduplicates on effect ID,
persists the point, allocates the next ordinal, and journals ChartPointAdded as
a terminal fact in the same turn. It does not put ChartPointAdded in the
routing outbox. ChartProjectionEndpoint exposes only an owner-matched snapshot.

ChartProjectionRoutes adds exactly one trusted host route:

~~~text
GET /poc/charts/{chartId}
Authorization: Bearer <opaque owner-session token>
~~~

The route asks TestOwnerAuthority to resolve the principal, then asks
ChartProjectionEndpoint for that principal’s snapshot. It returns 401 for an
unknown token and 404 when the authenticated owner does not own the chart. It
never accepts an owner ID from a query, path, or candidate.

- [ ] **Step 4: Implement the minimal Flutter client**

From the empty poc/flutter/chart_poc directory, run:

~~~powershell
flutter create --platforms=windows --project-name chart_poc .
~~~

Keep the generated windows/ runner. In pubspec.yaml declare these SDK
dependencies:

~~~yaml
dependencies:
  flutter:
    sdk: flutter
dev_dependencies:
  flutter_test:
    sdk: flutter
  integration_test:
    sdk: flutter
~~~

ChartProjection is a narrow client
abstraction; ChartScreen renders projection points with standard Flutter
widgets and a small trusted CustomPainter-based point plot, not merely a text
list. ChartProjectionHttpClient uses dart:io HttpClient for the
Windows-desktop POC to call GET /poc/charts/{chartId} and supplies only the
opaque session token returned by the trusted test harness. The widget test
supplies FakeChartProjection; Task 8 uses the HTTP client against a live host.

This uses the standard official Flutter widget-test pattern:
[build with pumpWidget and assert through a Finder](https://docs.flutter.dev/cookbook/testing/widget/introduction).

- [ ] **Step 5: Run chart gates**

Run: dotnet test poc/tests/DigitalBrain.Poc.Runtime.Tests -c Release

Expected: PASS, including owner and effect-ID deduplication.

Run: cd poc/flutter/chart_poc; dart analyze; flutter test

Expected: 0 analyzer errors and PASS.

## Task 5: Build the Roslyn-only Creator and policy gate

**Files:**

- Create: poc/src/DigitalBrain.Poc.Creator/ElonChartAuthoringIntent.cs
- Create: poc/src/DigitalBrain.Poc.Creator/FixedCandidateHeader.cs
- Create: poc/src/DigitalBrain.Poc.Creator/ElonChartSyntaxFactory.cs
- Create: poc/src/DigitalBrain.Poc.Creator/CandidateSemanticPolicy.cs
- Create: poc/src/DigitalBrain.Poc.Creator/CandidateSourceValidator.cs
- Create: poc/src/DigitalBrain.Poc.Creator/CandidateShape.cs
- Create: poc/tests/DigitalBrain.Poc.Creator.Tests/CreatorFacts.cs
- Create: poc/tests/DigitalBrain.Poc.Creator.Tests/ForbiddenSyntaxFacts.cs

**Consumes:** Task 2 ABI, Task 3 candidate-family identity, and Task 4
trusted contracts.

**Produces:** deterministic generation of one normal module and a
resolved-symbol allowlist. POC-0 uses a fixture intent, not an LLM.

- [ ] **Step 1: Write failing Creator shape tests**

~~~csharp
[Fact]
public void CreatorProducesTheExactNormalModuleShape()
{
    var result = _creator.Create(new ElonChartAuthoringIntent(
        CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa"),
        "elon-chart",
        "elonmusk"));

    Assert.Single(result.SourceFiles);
    Assert.Contains("ElonPostMatched", result.Source);
    Assert.Contains("ElonPostRuleNeuron", result.Source);
    Assert.Contains("ChartForwarderNeuron", result.Source);
    Assert.DoesNotContain("ChartNeuron", result.Source);
    Assert.DoesNotContain("ScriptedNeuron", result.Source);
    Assert.Contains(
        "AssemblyName=DigitalBrain.Poc.Candidate.cf_aaaaaaaaaaaaaaaaaaaaaaaaaa",
        result.Source);
}

[Fact]
public void SameIntentProducesTheSameSourceAndHash()
{
    var one = _creator.Create(ElonChartAuthoringIntent.Default);
    var two = _creator.Create(ElonChartAuthoringIntent.Default);

    Assert.Equal(one.Source, two.Source);
    Assert.Equal(one.SourceHash, two.SourceHash);
}
~~~

- [ ] **Step 2: Write the failing policy-mutation matrix**

Start from a valid Roslyn tree and inject each form as a syntax node. The
validator returns a typed error, not a warning:

~~~csharp
public static IEnumerable<object[]> ForbiddenForms =>
[
    [ "System.IO.File.ReadAllText", CandidatePolicyError.ForbiddenSymbol ],
    [ "System.Net.Http.HttpClient", CandidatePolicyError.ForbiddenSymbol ],
    [ "System.Diagnostics.Process.Start", CandidatePolicyError.ForbiddenSymbol ],
    [ "System.Environment.GetEnvironmentVariable", CandidatePolicyError.ForbiddenSymbol ],
    [ "System.Console.WriteLine", CandidatePolicyError.ForbiddenSymbol ],
    [ "System.Reflection.Assembly", CandidatePolicyError.ForbiddenSymbol ],
    [ "typeof", CandidatePolicyError.ForbiddenConstruct ],
    [ "GetType", CandidatePolicyError.ForbiddenSymbol ],
    [ "object", CandidatePolicyError.ForbiddenSymbol ],
    [ "ServiceProvider", CandidatePolicyError.ForbiddenSymbol ],
    [ "GrainFactory", CandidatePolicyError.ForbiddenSymbol ],
    [ "IGrainBase", CandidatePolicyError.ForbiddenSymbol ],
    [ "Task.Run", CandidatePolicyError.ForbiddenSymbol ],
    [ "System.Threading.Timer", CandidatePolicyError.ForbiddenSymbol ],
    [ "Parallel.For", CandidatePolicyError.ForbiddenSymbol ],
    [ "for", CandidatePolicyError.ForbiddenConstruct ],
    [ "foreach", CandidatePolicyError.ForbiddenConstruct ],
    [ "while", CandidatePolicyError.ForbiddenConstruct ],
    [ "dynamic", CandidatePolicyError.ForbiddenConstruct ],
    [ "unsafe", CandidatePolicyError.ForbiddenConstruct ],
    [ "DllImport", CandidatePolicyError.ForbiddenSymbol ],
    [ "top-level statement", CandidatePolicyError.ForbiddenConstruct ],
    [ "recursive helper", CandidatePolicyError.RecursiveCall ],
    [ "static initializer", CandidatePolicyError.ForbiddenConstruct ],
    [ "ModuleInitializer", CandidatePolicyError.ForbiddenConstruct ],
    [ "#:package", CandidatePolicyError.FixedHeaderMismatch ],
    [ "#:include", CandidatePolicyError.FixedHeaderMismatch ],
    [ "changed #:sdk", CandidatePolicyError.FixedHeaderMismatch ],
    [ "changed #:project", CandidatePolicyError.FixedHeaderMismatch ],
    [ "changed #:property", CandidatePolicyError.FixedHeaderMismatch ],
    [ "unapproved constructor service", CandidatePolicyError.ForbiddenConstructor ],
    [ "IHandle<ChartPointAdded>", CandidatePolicyError.UnauthorizedTrigger ],
    [ "new SocialPostObserved", CandidatePolicyError.UnauthorizedOutput ],
    [ "trusted alias collision", CandidatePolicyError.AliasCollision ],
    [ "foreign AddChartPoint", CandidatePolicyError.UnauthorizedTarget ],
];
~~~

- [ ] **Step 3: Run Creator tests to confirm they are red**

Run: dotnet test poc/tests/DigitalBrain.Poc.Creator.Tests -c Release

Expected: FAIL because no Creator or semantic policy exists.

- [ ] **Step 4: Implement intent-to-AST generation**

ElonChartAuthoringIntent receives a trusted CandidateFamilyId, attested
trigger grant (SocialPostObserved only for this POC), granted chart ID, expected
author, and local-synapse schema version. The control plane binds the family to
an authenticated owner before invoking the Creator. The intent contains no C#
text or caller-supplied owner identity.
ElonChartSyntaxFactory builds:

1. the fixed header, including host-generated family-qualified AssemblyName;
2. a family-qualified namespace, ElonPostMatched, and ElonPostRuleState;
3. a rule handler that compares author, advances typed state, and fires
   ElonPostMatched; trusted ingress owns source-post deduplication;
4. a forwarder that fires host-owned AddChartPoint with a ChartPointDraft for
   the granted chart.

It emits no direct Flutter, filesystem, network, service-location, or raw host
reference.

- [ ] **Step 5: Implement source and semantic validation**

Reparse persisted source, compare it with FixedCandidateHeader, compile with
only trusted metadata references, and verify:

- exactly one source tree and only approved using directives/types;
- ordinary Neuron base plus exact IHandle<T>;
- every handled input contract is in the candidate’s attested trigger grant;
- known aliases with no trusted-module collision;
- family-qualified assembly, namespace, and local-alias identity;
- exact GenerateSerializer/Alias/member-Id declaration shape for local synapse
  and durable state, with contiguous host-generated member IDs;
- allowed syntax and resolved symbols only;
- IDigitalBrain and IDurableState<T> as the only constructor services;
- an acyclic approved-helper call graph;
- no unapproved built-assembly reference.

- [ ] **Step 6: Run Creator gates**

Run: dotnet test poc/tests/DigitalBrain.Poc.Creator.Tests -c Release

Expected: PASS. Every mutation fails before a candidate directory or assembly
is published.

## Task 6: Compile, attest, and quarantine candidate modules

**Files:**

- Create: poc/src/DigitalBrain.Poc.Creator/FileCandidateCompiler.cs
- Create: poc/src/DigitalBrain.Poc.Creator/CandidateManifest.cs
- Create: poc/src/DigitalBrain.Poc.Creator/CandidateRepository.cs
- Create: poc/src/DigitalBrain.Poc.Creator/QuarantineRunner.cs
- Create: poc/src/DigitalBrain.Poc.ControlPlane/CandidateAttestation.cs
- Create: poc/src/DigitalBrain.Poc.ControlPlane/AttestationSigner.cs
- Create: poc/src/DigitalBrain.Poc.ControlPlane/TrustedCandidateCatalogStore.cs
- Create: poc/tests/DigitalBrain.Poc.Creator.Tests/CandidateCompilerFacts.cs
- Create: poc/tests/DigitalBrain.Poc.Creator.Tests/ControlPlaneAttestationFacts.cs
- Create: poc/tests/DigitalBrain.Poc.Acceptance.Tests/QuarantineFacts.cs

**Consumes:** Task 5 valid source/policy and Tasks 2–4 host bootstrap.

**Produces:** immutable source, managed IL, an evidence mirror in the
candidate directory, a signed control-plane attestation outside it, and an
isolated scenario result. Failed candidates never become approvable.

- [ ] **Step 1: Write failing compilation and quarantine tests**

~~~csharp
[Fact]
public async Task ValidCandidateCreatesOneFileAndAnExternallyAttestedManagedAssembly()
{
    var compiled = await _compiler.CompileAsync(ElonChartAuthoringIntent.Default);
    var candidate = await _quarantine.RunAsync(compiled);

    Assert.Equal("elon-chart.cs", Path.GetFileName(candidate.SourcePath));
    Assert.Single(Directory.EnumerateFiles(
        candidate.Directory,
        "*.cs",
        SearchOption.AllDirectories));
    Assert.Empty(Directory.EnumerateFiles(
        candidate.Directory,
        "*.csproj",
        SearchOption.AllDirectories));
    Assert.True(candidate.Manifest.SourceHashVerified);
    Assert.True(candidate.Manifest.AssemblyHashVerified);
    Assert.Equal(CandidateStatus.AwaitingOwnerApproval, candidate.Manifest.Status);

    var attestation = await _controlPlane.ReadAttestationAsync(candidate.Id);
    Assert.True(_attestationVerifier.Verify(attestation));
    Assert.Equal(candidate.Manifest.SourceHash, attestation.Payload.SourceHash);
    Assert.Equal(candidate.Manifest.AssemblyHash, attestation.Payload.AssemblyHash);
    Assert.Equal(
        candidate.Manifest.CandidateMetadataHash,
        attestation.Payload.CandidateMetadataHash);
}

[Fact]
public async Task QuarantineRoutesAcrossBothGeneratedNeurons()
{
    var compiled = await _compiler.CompileAsync(ElonChartAuthoringIntent.Default);
    var result = await _quarantine.RunAsync(compiled);

    Assert.Equal(
        ["SocialPostObserved", "ElonPostMatched", "AddChartPoint", "ChartPointAdded"],
        result.JournalKindsForInput("post-1"));
    Assert.Equal(1, result.ChartPointCount);
    Assert.True(result.AttestationSignatureVerified);
    Assert.Null(await _controlPlane.ActiveAsync(
        result.AuthenticatedOwner,
        result.CandidateFamily));
}

[Fact]
public async Task MutableCandidateMetadataCannotReplaceTheExternalAttestation()
{
    var compiled = await _compiler.CompileAsync(ElonChartAuthoringIntent.Default);
    var candidate = await _quarantine.RunAsync(compiled);
    await _candidates.ReplaceEvidenceMirrorAsync(
        candidate.Id,
        "{\"sourceHash\":\"attacker-chosen\"}");

    var result = await _controlPlane.VerifyForBootAsync(candidate.Id);

    Assert.False(result.Succeeded);
    Assert.Equal(AttestationFailure.CandidateMetadataHash, result.Failure);
}

[Fact]
public async Task DisposingQuarantineRunDeletesCandidateAndControlPlaneEvidence()
{
    var run = PocDataRoot.Create();
    var compiled = await _compiler.CompileAsync(
        ElonChartAuthoringIntent.Default,
        run);
    var candidate = await _quarantine.RunAsync(compiled, run);

    await run.DisposeAsync();

    Assert.False(Directory.Exists(candidate.Directory));
    Assert.False(await _controlPlane.ExistsAsync(candidate.Id));
    Assert.Empty(await PocDataRoot.FindArtifactsForRunAsync(
        PocPaths.Root,
        run.RunId));
}
~~~

- [ ] **Step 2: Run tests to confirm they are red**

Run: dotnet test poc/tests/DigitalBrain.Poc.Creator.Tests -c Release

Expected: FAIL because compiler/repository do not exist.

Run: dotnet test poc/tests/DigitalBrain.Poc.Acceptance.Tests -c Release

Expected: FAIL because quarantine does not exist.

- [ ] **Step 3: Implement immutable candidate publication**

Write canonical UTF-8 source once to
candidates/<run-id>/<source-hash>/elon-chart.cs, invoke the real file build, verify
managed IL and hash, and write derived candidate.json. CandidateRepository owns
only this candidate directory; candidate.json is an evidence mirror, never an
authority. The compiler derives source, AST, fixed-header, compiler, SDK,
references, capabilities, contracts, state schema, assembly, and candidate-json
hashes. Callers cannot supply them.

The temporary SDK virtual-project/build scratch is outside the persisted
candidate directory. Copy only verified managed IL and canonical evidence into
that directory; recursively reject a persisted candidate project file.

- [ ] **Step 4: Implement trusted quarantine**

Start a new host process with the candidate loaded before Orleans startup.
Fire trusted SocialPostObserved through a trusted scenario harness. Quarantine
has a disposable state root and no production credentials or live Flutter
effect. It checks the full fixed-header/source/IL/reference/capability evidence
before it starts the process, then proves the candidate-local synapse and
trusted chart route. Invalid build, serialization, route, or scenario outcome
records diagnostics and leaves the active pointer unchanged.

Only a successful scenario becomes an attestation payload. AttestationSigner
signs its canonical payload with a P-256 test key held by TestOwnerAuthority
outside candidates/ and control-plane-store/. TrustedCandidateCatalogStore
writes the signed immutable attestation under
control-plane-store/<run-id>/attestations/.
It includes the candidate evidence-mirror hash, so changing candidate.json is
detected even though that mirror cannot grant authority. A failed quarantine
gets no signed attestation and cannot advance to approval; a successful one
advances to AwaitingOwnerApproval, never directly to Active.

- [ ] **Step 5: Run compilation and quarantine gates**

Run: dotnet test poc/tests/DigitalBrain.Poc.Creator.Tests -c Release

Expected: PASS.

Run: dotnet test poc/tests/DigitalBrain.Poc.Acceptance.Tests -c Release

Expected: PASS with journal order SocialPostObserved → ElonPostMatched →
AddChartPoint → ChartPointAdded for the accepted input, exactly one chart
point, a verified external attestation, no active pointer, and teardown removal
of the candidate/control-plane evidence.

## Task 7: Add owner-gated restart-only promotion and rollback

**Files:**

- Create: poc/src/DigitalBrain.Poc.Runtime/CandidateLifecycle.cs
- Create: poc/src/DigitalBrain.Poc.Runtime/CandidateCatalog.cs
- Create: poc/src/DigitalBrain.Poc.ControlPlane/OwnerApproval.cs
- Create: poc/src/DigitalBrain.Poc.ControlPlane/OwnerApprovalSigner.cs
- Create: poc/src/DigitalBrain.Poc.ControlPlane/ActiveCandidatePointer.cs
- Create: poc/src/DigitalBrain.Poc.ControlPlane/PointerSigner.cs
- Create: poc/src/DigitalBrain.Poc.ControlPlane/CandidatePointerHead.cs
- Create: poc/src/DigitalBrain.Poc.Host/HostSupervisor.cs
- Create: poc/src/DigitalBrain.Poc.Host/IngressQuiesceGate.cs
- Create: poc/src/DigitalBrain.Poc.Host/IngressAdmissionLease.cs
- Create: poc/tests/DigitalBrain.Poc.Runtime.Tests/CandidateCatalogFacts.cs
- Create: poc/tests/DigitalBrain.Poc.Acceptance.Tests/PromotionFacts.cs

**Consumes:** Tasks 3 and 6.

**Produces:** signed authenticated-owner approval, signed immutable
active/previous pointers keyed by owner and candidate family, and restart-only
promotion/rollback with tamper checks.

- [ ] **Step 1: Write failing lifecycle tests**

~~~csharp
[Fact]
public async Task OnlyBoundOwnerCanApproveExactAttestedRecord()
{
    var ownerA = _owners.PrincipalForTest("owner-a");
    var ownerB = _owners.PrincipalForTest("owner-b");
    var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
    var candidate = await _candidates.CreateAwaitingOwnerApprovalAsync(
        ownerA,
        family);

    await Assert.ThrowsAsync<AuthorizationException>(
        () => _catalog.ApproveAsync(ownerB, candidate.Id));

    await _catalog.ApproveAsync(ownerA, candidate.Id);
    Assert.Equal(
        CandidateStatus.ApprovedInactive,
        await _catalog.StatusAsync(candidate.Id));
    Assert.True(_approvalVerifier.Verify(
        await _controlPlane.ReadApprovalAsync(candidate.Id)));
}

[Fact]
public async Task TamperedCandidateRefusesColdStartAndKeepsPriorActiveHash()
{
    var ownerA = _owners.PrincipalForTest("owner-a");
    var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
    var active = await _catalog.ActiveAsync(ownerA, family);
    var proposed = await _candidates.CreateApprovedAsync(ownerA, family);
    await File.AppendAllTextAsync(proposed.AssemblyPath, "tamper");

    var start = await _supervisor.PromoteAsync(ownerA, proposed.Id);

    Assert.False(start.Succeeded);
    Assert.Equal(
        active.SourceHash,
        (await _catalog.ActiveAsync(ownerA, family)).SourceHash);
}

[Fact]
public async Task ApprovedInactiveCandidateCannotBeSelectedByNormalBoot()
{
    var ownerA = _owners.PrincipalForTest("owner-a");
    var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
    var prior = await _catalog.ActiveAsync(ownerA, family);
    var inactive = await _candidates.CreateApprovedAsync(ownerA, family);

    var boot = await HostProcess.TryRestartActiveAsync(_stateRoot, _controlPlane);

    Assert.True(boot.Succeeded);
    Assert.Equal(prior.SourceHash, boot.ActiveSourceHash);
    Assert.NotEqual(inactive.SourceHash, boot.ActiveSourceHash);
}

[Theory]
[InlineData(CandidateTamper.Source)]
[InlineData(CandidateTamper.Assembly)]
[InlineData(CandidateTamper.CandidateMetadata)]
[InlineData(CandidateTamper.FixedHeader)]
[InlineData(CandidateTamper.FixedReference)]
[InlineData(CandidateTamper.CapabilityGrant)]
[InlineData(CandidateTamper.QuarantineEvidence)]
[InlineData(CandidateTamper.SignedAttestation)]
[InlineData(CandidateTamper.SignedApproval)]
public async Task AnyAttestedPartTamperedAfterApprovalRefusesPromotion(
    CandidateTamper tamper)
{
    var ownerA = _owners.PrincipalForTest("owner-a");
    var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
    var prior = await _catalog.ActiveAsync(ownerA, family);
    var priorHead = await _controlPlane.ReadPointerHeadAsync(ownerA, family);
    var proposed = await _candidates.CreateApprovedAsync(ownerA, family);
    await _candidates.TamperAsync(proposed.Id, tamper);

    var result = await _supervisor.PromoteAsync(ownerA, proposed.Id);

    Assert.False(result.Succeeded);
    Assert.Equal(
        prior.SourceHash,
        (await _catalog.ActiveAsync(ownerA, family)).SourceHash);
    Assert.Equal(
        priorHead,
        await _controlPlane.ReadPointerHeadAsync(ownerA, family));
}

[Fact]
public async Task ReplayedPreviouslyValidSignedPointerCannotBoot()
{
    var ownerA = _owners.PrincipalForTest("owner-a");
    var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
    var v1 = await _fixtures.CreateAndPromoteAsync(ownerA, family, LocalSchema.V1);
    var oldPointer = await _controlPlane.ReadPointerAsync(ownerA, family);
    Assert.Equal(v1.SourceHash, oldPointer.CandidateSourceHash);
    var v2 = await _fixtures.CreateApprovedAsync(
        ownerA,
        family,
        LocalSchema.V1,
        BehaviorRevision.ChangedRule);
    await _supervisor.PromoteAsync(ownerA, v2.Id);

    await _controlPlane.ReplacePointerFileForTestAsync(ownerA, family, oldPointer);
    var boot = await HostProcess.TryRestartActiveAsync(_stateRoot, _controlPlane);

    Assert.False(boot.Succeeded);
    Assert.Equal(BootFailure.StaleOrReplayedPointer, boot.Failure);
    Assert.Equal(
        v2.SourceHash,
        (await _catalog.ActiveAsync(ownerA, family)).SourceHash);
}

[Fact]
public async Task InvalidPointerSignatureCannotBootWhenItsPayloadStillMatchesTheHead()
{
    var ownerA = _owners.PrincipalForTest("owner-a");
    var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
    var current = await _fixtures.CreateAndPromoteAsync(ownerA, family, LocalSchema.V1);
    var pointer = await _controlPlane.ReadPointerAsync(ownerA, family);
    await _controlPlane.ReplacePointerFileForTestAsync(
        ownerA,
        family,
        pointer with { Signature = "corrupt-detached-signature" });

    var boot = await HostProcess.TryRestartActiveAsync(_stateRoot, _controlPlane);

    Assert.False(boot.Succeeded);
    Assert.Equal(BootFailure.InvalidPointerSignature, boot.Failure);
    Assert.Equal(
        current.SourceHash,
        (await _catalog.ActiveAsync(ownerA, family)).SourceHash);
}

[Fact]
public async Task PointerHeadCompareAndSwapLetsOnlyOneSameVersionAdvance()
{
    var ownerA = _owners.PrincipalForTest("owner-a");
    var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
    await _fixtures.CreateAndPromoteAsync(ownerA, family, LocalSchema.V1);
    var head = await _controlPlane.ReadPointerHeadAsync(ownerA, family);
    var first = await _fixtures.CreateApprovedAsync(
        ownerA, family, LocalSchema.V1, BehaviorRevision.ChangedRule);
    var second = await _fixtures.CreateApprovedAsync(
        ownerA, family, LocalSchema.V1, BehaviorRevision.ChangedRuleAgain);
    var firstPointer = _pointerSigner.Sign(
        ActiveCandidatePointer.Next(head, first.SourceHash));
    var secondPointer = _pointerSigner.Sign(
        ActiveCandidatePointer.Next(head, second.SourceHash));

    var results = await Task.WhenAll(
        _controlPlane.TryAdvancePointerHeadAsync(head, firstPointer),
        _controlPlane.TryAdvancePointerHeadAsync(head, secondPointer));

    Assert.Single(results.Where(result => result.Succeeded));
    var updated = await _controlPlane.ReadPointerHeadAsync(ownerA, family);
    Assert.Equal(head.Version + 1, updated.Version);
    Assert.Contains(
        updated.CurrentPayloadHash,
        [firstPointer.PayloadHash, secondPointer.PayloadHash]);
}

[Fact]
public async Task FailedChildPreflightNeverPublishesANewPointer()
{
    var ownerA = _owners.PrincipalForTest("owner-a");
    var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
    var prior = await _catalog.ActiveAsync(ownerA, family);
    var priorHead = await _controlPlane.ReadPointerHeadAsync(ownerA, family);
    var proposed = await _candidates.CreateApprovedAsync(ownerA, family);

    var result = await _supervisor.PromoteAsync(
        ownerA,
        proposed.Id,
        fault: HostFault.BeforeCandidateChildReady);

    Assert.False(result.Succeeded);
    Assert.Equal(prior.SourceHash, (await _catalog.ActiveAsync(ownerA, family)).SourceHash);
    Assert.Equal(
        priorHead,
        await _controlPlane.ReadPointerHeadAsync(ownerA, family));
}

[Fact]
public async Task IngressAdmissionLeaseClosesThePromotionRaceBeforePointerAdvance()
{
    var ownerA = _owners.PrincipalForTest("owner-a");
    var ownerSession = _owners.SessionFor("owner-a");
    var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
    var current = await _fixtures.CreateAndPromoteAsync(ownerA, family, LocalSchema.V1);
    var proposed = await _fixtures.CreateApprovedAsync(
        ownerA,
        family,
        LocalSchema.V1,
        BehaviorRevision.ChangedRule);
    await using var oldHost = await HostProcess.AttachCurrentAsync(
        _supervisor,
        ownerA,
        family);

    // The lease models a request that passed the admission boundary before
    // quiescing, but has not yet queued its turn.
    await using var preCloseLease = await oldHost.AcquireIngressLeaseForTestAsync(
        ownerSession);
    var priorHead = await _controlPlane.ReadPointerHeadAsync(ownerA, family);
    var promotion = _supervisor.BeginPromotionAsync(
        ownerA,
        proposed.Id,
        fault: HostFault.PauseAfterIngressClosedBeforeDrain);
    await _supervisor.WaitUntilIngressClosedAsync(ownerA, family);
    Assert.False(promotion.IsCompleted);
    Assert.Equal(
        priorHead,
        await _controlPlane.ReadPointerHeadAsync(ownerA, family));

    await Assert.ThrowsAsync<HostQuiescingException>(
        () => oldHost.FireTrustedAsync(
            ownerSession,
            new SocialPostObserved("late-post", "elonmusk", _clock.UtcNow)));
    Assert.Empty(await oldHost.JournalKindsForInputAsync("late-post"));

    // Let the supervisor begin draining. The held pre-close lease must still
    // prevent pointer advancement until it queues and completes its turn.
    _supervisor.ReleaseTestFault();
    Assert.False(promotion.IsCompleted);
    Assert.Equal(
        priorHead,
        await _controlPlane.ReadPointerHeadAsync(ownerA, family));

    // Work admitted before closure remains counted and must drain before the
    // pointer can advance; it is never silently stranded behind the gate.
    await preCloseLease.FireAsync(
        new SocialPostObserved("pre-close-post", "elonmusk", _clock.UtcNow));
    Assert.Equal(
        new[]
        {
            "SocialPostObserved", "ElonPostMatched", "AddChartPoint", "ChartPointAdded"
        },
        await oldHost.JournalKindsForInputAsync("pre-close-post"));
    Assert.Empty(await _outbox.PendingTargetingCandidateRevisionAsync(
        ownerA,
        family,
        current.SourceHash));

    Assert.True((await promotion).Succeeded);
}

[Fact]
public async Task IncompatibleLocalSchemaCannotReplaceAFamilyWithRetainedJournal()
{
    var ownerA = _owners.PrincipalForTest("owner-a");
    var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
    var v1 = await _fixtures.CreateAndPromoteAsync(ownerA, family, LocalSchema.V1);

    await using (var host = await HostProcess.RestartActiveAndAttachAsync(
        _stateRoot,
        _controlPlane))
    {
        await host.FireTrustedAsync(
            _owners.SessionFor("owner-a"),
            new SocialPostObserved("post-1", "elonmusk", _clock.UtcNow));
    }

    var v2 = await _fixtures.CreateApprovedAsync(ownerA, family, LocalSchema.V2);
    var result = await _supervisor.PromoteAsync(ownerA, v2.Id);

    Assert.False(result.Succeeded);
    Assert.Equal(PromotionFailure.IncompatibleRetainedSchema, result.Failure);
    Assert.Equal(
        v1.SourceHash,
        (await _catalog.ActiveAsync(ownerA, family)).SourceHash);
}

[Fact]
public async Task PromotionAndRollbackRefuseWhileCurrentRevisionHasPendingGeneratedLocalEnvelope()
{
    var ownerA = _owners.PrincipalForTest("owner-a");
    var ownerSession = _owners.SessionFor("owner-a");
    var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
    var prior = await _fixtures.CreateAndPromoteAsync(ownerA, family, LocalSchema.V1);
    var current = await _fixtures.CreateApprovedAsync(
        ownerA,
        family,
        LocalSchema.V1,
        BehaviorRevision.ChangedRule);
    var activated = await _supervisor.PromoteAsync(
        ownerA,
        current.Id,
        fault: HostFault.AfterGeneratedLocalOutboxCommitBeforeForwarderAcknowledgement);
    Assert.True(activated.Succeeded);

    await using (var host = await HostProcess.AttachAsync(activated))
    {
        await host.FireTrustedAsync(
            ownerSession,
            new SocialPostObserved("post-1", "elonmusk", _clock.UtcNow));
        await host.WaitForExitAsync();
    }

    var next = await _fixtures.CreateApprovedAsync(
        ownerA,
        family,
        LocalSchema.V1,
        BehaviorRevision.ChangedRuleAgain);
    var promotion = await _supervisor.PromoteAsync(ownerA, next.Id);
    var rollback = await _supervisor.RollbackAsync(ownerA, family);

    Assert.False(promotion.Succeeded);
    Assert.Equal(PromotionFailure.PendingCandidateTargetedOutbox, promotion.Failure);
    Assert.False(rollback.Succeeded);
    Assert.Equal(PromotionFailure.PendingCandidateTargetedOutbox, rollback.Failure);
    Assert.Equal(
        prior.SourceHash,
        (await _catalog.PreviousAsync(ownerA, family)).SourceHash);
    Assert.Equal(
        current.SourceHash,
        (await _catalog.ActiveAsync(ownerA, family)).SourceHash);
}

[Fact]
public async Task PromotionRefusesWhileTrustedFanOutTargetsTheCurrentRevision()
{
    var ownerA = _owners.PrincipalForTest("owner-a");
    var ownerSession = _owners.SessionFor("owner-a");
    var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
    var current = await _fixtures.CreateApprovedAsync(ownerA, family, LocalSchema.V1);
    var activated = await _supervisor.PromoteAsync(
        ownerA,
        current.Id,
        fault: HostFault.AfterTrustedFanOutCommitBeforeRuleAcknowledgement);
    Assert.True(activated.Succeeded);

    await using (var host = await HostProcess.AttachAsync(activated))
    {
        await host.FireTrustedAsync(
            ownerSession,
            new SocialPostObserved("post-1", "elonmusk", _clock.UtcNow));
        await host.WaitForExitAsync();
    }

    Assert.Single(await _outbox.PendingTargetingCandidateRevisionAsync(
        ownerA,
        family,
        current.SourceHash));
    var proposed = await _fixtures.CreateApprovedAsync(
        ownerA,
        family,
        LocalSchema.V1,
        BehaviorRevision.ChangedRule);
    var rejected = await _supervisor.PromoteAsync(ownerA, proposed.Id);

    Assert.False(rejected.Succeeded);
    Assert.Equal(PromotionFailure.PendingCandidateTargetedOutbox, rejected.Failure);
    Assert.Equal(
        current.SourceHash,
        (await _catalog.ActiveAsync(ownerA, family)).SourceHash);

    await using (var oldRevision = await HostProcess.RestartActiveAndAttachAsync(
        _stateRoot,
        _controlPlane))
    {
        Assert.Equal(current.SourceHash, oldRevision.ActiveSourceHash);
        Assert.Equal(
            [1],
            (await oldRevision.ChartAsync(ownerSession, "elon-chart")).Ordinals);
    }

    Assert.True((await _supervisor.PromoteAsync(ownerA, proposed.Id)).Succeeded);
}
~~~

- [ ] **Step 2: Run test to confirm it is red**

Run: dotnet test poc/tests/DigitalBrain.Poc.Runtime.Tests -c Release

Expected: FAIL because lifecycle and ownership checks do not exist.

- [ ] **Step 3: Implement state transitions and supervisor**

Legal lifecycle: Draft → Validated → Quarantined → AwaitingOwnerApproval →
ApprovedInactive → Active, with Active → RolledBack. CandidateCatalog receives an
AuthenticatedPrincipal, never an OwnerId string, and checks that principal
against the signed attestation’s owner/family/ID/hash before it issues an
OwnerApproval. OwnerApprovalSigner signs that exact approval record. Store an
ActiveCandidatePointer containing owner, family, current hash, previous hash,
parent canonical-payload hash, and monotonically increasing version.
PointerSigner signs that canonical payload with a P-256 control-plane key held
outside candidate/control-plane data roots; the detached signature is verified
before any boot or head comparison. The trusted store keeps
CandidatePointerHead for each owner/family, containing the canonical payload
hash, version, and parent hash. It atomically compare-and-swaps only the
expected head to a pointer with the next version and matching parent hash; a
rollback writes a fresh higher-version pointer to the prior candidate, never
restores an old file. Boot rejects an invalid pointer signature even if its
unsigned payload still matches the head, and otherwise refuses a stale or
replayed pointer.

HostSupervisor must not mutate a running catalog. IngressQuiesceGate returns
an IngressAdmissionLease only by atomically checking that the gate is open and
registering the lease in its in-flight count, before any trusted request can
enqueue a turn. Promotion and rollback first close that gate on the old host:
closure prevents all new leases, while every pre-close lease remains registered
until its turn has been queued or the request has failed without queuing.
Consuming IngressAdmissionLease.FireAsync transfers that registration to its
queued turn; disposing an unused lease removes it without enqueueing anything.
PauseAfterIngressClosedBeforeDrain is a test-only barrier immediately after
that atomic closure and before the supervisor begins draining, child startup,
or pointer-head comparison. New trusted ingress receives a retryable
HostQuiescingException; it cannot enqueue a new old-revision turn. The
supervisor waits for the registered leases, their in-flight turns, and every
outbox envelope targeting the current candidate revision, including a trusted
fan-out envelope as well as candidate-local traffic, to drain. If any cannot
drain, it reopens ingress, refuses the transition, and leaves the pointer
alone. The coordinated test above holds a pre-close lease while promotion
starts, proves a post-close request is rejected, proves the pointer head is
unchanged at the barrier, and proves the pointer cannot advance until that
pre-close work drains. The forced trusted-fan-out crash test proves a
cross-revision envelope causes refusal until the old pointer can drain it.
Only then does it start a proposed child in a
no-input verification state. That child verifies every manifest hash and the
candidate evidence-mirror hash, external attestation, signed owner approval,
capability grant, quarantine scenario, and schema compatibility before it
configures the selected candidate as an application part and reports ready.
Only then does TrustedCandidateCatalogStore perform the pointer-head
compare-and-swap, after which the old host stops and the ready child opens
ingress. Any failure reopens old-host ingress and leaves the last known-good
pointer/head unchanged.

Normal Program startup accepts a state root and trusted control-plane root, not
a candidate path, assembly name, or source hash argument. It enumerates only
the signed active pointers that match their trusted heads. The separate
Task-3 fixture and Task-6 quarantine modes are explicitly test-only and cannot
be selected through the normal host command line.

TestFaults defines PauseAfterIngressClosedBeforeDrain,
BeforeCandidateChildReady,
AfterTrustedFanOutCommitBeforeRuleAcknowledgement, and
AfterGeneratedLocalOutboxCommitBeforeForwarderAcknowledgement in this task’s
test configuration; all are unreachable from candidate or normal host
configuration. Task 8 adds its chart-acknowledgement fault to the same sealed
test seam.

- [ ] **Step 4: Write the PID proof**

~~~csharp
[Fact]
public async Task PromotionAndRollbackBothStartNewProcesses()
{
    var ownerA = _owners.PrincipalForTest("owner-a");
    var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
    var before = await _supervisor.CurrentAsync(ownerA, family);
    var promoted = await _supervisor.PromoteAsync(ownerA, _candidateB.Id);
    var rolledBack = await _supervisor.RollbackAsync(ownerA, family);

    Assert.NotEqual(before.ProcessId, promoted.ProcessId);
    Assert.NotEqual(promoted.ProcessId, rolledBack.ProcessId);
    Assert.Equal(_candidateA.SourceHash, rolledBack.ActiveSourceHash);
}
~~~

The same compatibility preflight runs before promotion and rollback. POC-0 has
no schema migration: a candidate whose local synapse/state aliases cannot
deserialize the retained family journal is refused rather than promoted.
Rollback never discards outbox work or regenerates an artifact. A historical
or corrupted pointer to an incompatible artifact makes boot refuse before it
loads code.

- [ ] **Step 5: Run lifecycle gates**

Run: dotnet test poc/tests/DigitalBrain.Poc.Runtime.Tests -c Release

Expected: PASS.

Run: dotnet test poc/tests/DigitalBrain.Poc.Acceptance.Tests -c Release

Expected: PASS with changed PIDs, owner rejection, tamper refusal, and prior
artifact restoration. The candidate-A/candidate-B retained-journal test proves
incompatible local schema refusal; the faulted local-outbox and trusted-fan-out
tests prove a revision cannot be switched while any envelope targets it. The
invalid-signature, signed-pointer replay, and failed-child tests prove that
neither a forged pointer, stale pointer, nor unready child can become active.
The quiescing test proves no late ingress can create old-revision work between
gate closure, drain, and pointer advancement.

## Task 8: Prove end-to-end routing, crash recovery, and Flutter projection

**Files:**

- Create: poc/tests/DigitalBrain.Poc.Acceptance.Tests/ElonChartPocFacts.cs
- Create: poc/tests/DigitalBrain.Poc.Acceptance.Tests/CrashRecoveryFacts.cs
- Create: poc/flutter/chart_poc/integration_test/elon_chart_poc_test.dart
- Create: poc/flutter/chart_poc/integration_test/poc_host_fixture.dart
- Modify: poc/src/DigitalBrain.Poc.Host/Program.cs
- Modify: poc/src/DigitalBrain.Poc.Host/TestFaults.cs
- Modify: poc/src/DigitalBrain.Poc.Runtime/Outbox.cs
- Modify: poc/flutter/chart_poc/pubspec.yaml

**Consumes:** all preceding tasks.

**Produces:** black-box proof of matching/non-matching behavior, whole-process
restart, precisely named crash-between-commit-and-acknowledgement windows,
multi-owner family isolation, and a Flutter chart observation.

After Task 6, acceptance tests must never pass a candidate path or module
directly to a normal HostProcess start. PromoteAndAttachAsync delegates to
HostSupervisor.PromoteAsync and attaches to the process it cold-starts;
RestartActiveAndAttachAsync boots only from the verified signed active pointer
in TrustedCandidateCatalogStore. Quarantine remains the sole pre-approval
exception and is explicitly disposable.

Every Task 8 fact owns a fresh async-disposable PocDataRoot lease. In the
snippets, TemporaryStateRoot is that lease; its DisposeAsync removes the
run-scoped state/candidate/control-plane paths and fails the test if
PocDataRoot.FindArtifactsForRunAsync finds any residual record. The multi-owner
fixture is constructed from the same lease. No acceptance test shares a
candidate directory, journal, session, or control-plane root with another run.

- [ ] **Step 1: Write the failing end-to-end scenario**

~~~csharp
[Fact]
public async Task ApprovedModuleTurnsOnlyElonPostsIntoChartPointsAcrossRestart()
{
    await using var stateRoot = TemporaryStateRoot.Create();
    var ownerA = _owners.PrincipalForTest("owner-a");
    var ownerSession = _owners.SessionFor("owner-a");
    var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
    var firstObservedAt = _clock.UtcNow;
    var candidate = await _fixtures.CreateApprovedElonCandidateAsync(
        ownerA,
        family);

    await using var first = await _fixtures.PromoteAndAttachAsync(
        ownerA,
        candidate.Id,
        stateRoot);
    await first.FireTrustedAsync(
        ownerSession,
        new SocialPostObserved("post-1", "elonmusk", firstObservedAt));
    await first.FireTrustedAsync(
        ownerSession,
        new SocialPostObserved("post-2", "other", _clock.UtcNow));

    Assert.Equal([1], (await first.ChartAsync(ownerSession, "elon-chart")).Ordinals);
    Assert.Equal(
        ["SocialPostObserved", "ElonPostMatched", "AddChartPoint", "ChartPointAdded"],
        await first.JournalKindsForInputAsync("post-1"));
    Assert.Equal(
        ["SocialPostObserved"],
        await first.JournalKindsForInputAsync("post-2"));
    await first.TerminateAsync();

    await using var second = await HostProcess.RestartActiveAndAttachAsync(
        stateRoot,
        _controlPlane);
    await second.FireTrustedAsync(
        ownerSession,
        new SocialPostObserved("post-3", "elonmusk", _clock.UtcNow));
    await second.FireTrustedAsync(
        ownerSession,
        new SocialPostObserved("post-1", "elonmusk", firstObservedAt));

    Assert.Equal(
        [1, 2],
        (await second.ChartAsync(ownerSession, "elon-chart")).Ordinals);
    Assert.Equal(
        2,
        (await second.GeneratedStateAsync(ownerSession, family)).AcceptedCount);
    Assert.Equal(
        ["SocialPostObserved", "ElonPostMatched", "AddChartPoint", "ChartPointAdded"],
        await second.JournalKindsForInputAsync("post-3"));
    Assert.Equal(
        ["SocialPostObserved", "ElonPostMatched", "AddChartPoint", "ChartPointAdded"],
        await second.JournalKindsForInputAsync("post-1"));
    Assert.Equal(
        [
            "SocialPostObserved", "ElonPostMatched", "AddChartPoint", "ChartPointAdded",
            "SocialPostObserved",
            "SocialPostObserved", "ElonPostMatched", "AddChartPoint", "ChartPointAdded",
        ],
        await second.OrderedJournalKindsAsync());
}
~~~

- [ ] **Step 2: Write the failing crash-window test**

~~~csharp
[Fact]
public async Task RestartReplaysChartDeliveryCommittedBeforeUpstreamAcknowledgementExactlyOnce()
{
    await using var root = TemporaryStateRoot.Create();
    var ownerA = _owners.PrincipalForTest("owner-a");
    var ownerSession = _owners.SessionFor("owner-a");
    var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
    var candidate = await _fixtures.CreateApprovedElonCandidateAsync(
        ownerA,
        family);

    await using var first = await _fixtures.PromoteAndAttachAsync(
        ownerA,
        candidate.Id,
        root,
        HostFault.AfterChartNeuronCommitBeforeUpstreamOutboxAcknowledgement);
    await first.FireTrustedAsync(
        ownerSession,
        new SocialPostObserved("post-1", "elonmusk", _clock.UtcNow));
    await first.WaitForExitAsync();

    await using var second = await HostProcess.RestartActiveAndAttachAsync(
        root,
        _controlPlane);
    Assert.Equal(
        [1],
        (await second.ChartAsync(ownerSession, "elon-chart")).Ordinals);

    await second.ReplayLastDeliveryAsync();
    Assert.Equal(
        [1],
        (await second.ChartAsync(ownerSession, "elon-chart")).Ordinals);
}
~~~

- [ ] **Step 3: Write the failing generated-synapse restart test**

~~~csharp
[Fact]
public async Task RestartDeserializesAndDeliversCommittedGeneratedSynapse()
{
    await using var root = TemporaryStateRoot.Create();
    var ownerA = _owners.PrincipalForTest("owner-a");
    var ownerSession = _owners.SessionFor("owner-a");
    var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
    var candidate = await _fixtures.CreateApprovedElonCandidateAsync(
        ownerA,
        family);

    await using var first = await _fixtures.PromoteAndAttachAsync(
        ownerA,
        candidate.Id,
        root,
        HostFault.AfterGeneratedLocalOutboxCommitBeforeForwarderAcknowledgement);
    await first.FireTrustedAsync(
        ownerSession,
        new SocialPostObserved("post-1", "elonmusk", _clock.UtcNow));
    await first.WaitForExitAsync();

    await using var second = await HostProcess.RestartActiveAndAttachAsync(
        root,
        _controlPlane);
    Assert.Equal(
        [1],
        (await second.ChartAsync(ownerSession, "elon-chart")).Ordinals);
    Assert.Equal(
        ["SocialPostObserved", "ElonPostMatched", "AddChartPoint", "ChartPointAdded"],
        await second.JournalKindsForInputAsync("post-1"));
}
~~~

- [ ] **Step 4: Write the failing multi-owner family-isolation test**

~~~csharp
[Fact]
public async Task TwoOwnerFamiliesWithTheSameGeneratedTypeNamesRouteIndependently()
{
    await using var run = PocDataRoot.Create();
    var fixture = await PocFixture.CreateAsync(run);
    var ownerA = fixture.Owners.PrincipalForTest("owner-a");
    var ownerB = fixture.Owners.PrincipalForTest("owner-b");
    var sessionA = fixture.Owners.SessionFor("owner-a");
    var sessionB = fixture.Owners.SessionFor("owner-b");
    var familyA = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
    var familyB = CandidateFamilyId.Parse("cf_bbbbbbbbbbbbbbbbbbbbbbbbbb");
    var candidateA = await fixture.CreateApprovedAsync(ownerA, familyA);
    var candidateB = await fixture.CreateApprovedAsync(ownerB, familyB);

    await fixture.Supervisor.PromoteAsync(ownerA, candidateA.Id);
    var secondPromotion = await fixture.Supervisor.PromoteAsync(ownerB, candidateB.Id);
    await using var host = await HostProcess.AttachAsync(secondPromotion);
    await host.FireTrustedAsync(
        sessionA,
        new SocialPostObserved("owner-a-post", "elonmusk", _clock.UtcNow));
    await host.FireTrustedAsync(
        sessionB,
        new SocialPostObserved("owner-b-post", "elonmusk", _clock.UtcNow));

    Assert.NotEqual(candidateA.LocalSynapseAlias, candidateB.LocalSynapseAlias);
    Assert.Equal([1], (await host.ChartAsync(sessionA, "elon-chart")).Ordinals);
    Assert.Equal([1], (await host.ChartAsync(sessionB, "elon-chart")).Ordinals);
}
~~~

- [ ] **Step 5: Run acceptance tests to confirm they are red**

Run: dotnet test poc/tests/DigitalBrain.Poc.Acceptance.Tests -c Release

Expected: FAIL until the entire route and deterministic fault seam exist.

- [ ] **Step 6: Implement only the required test faults**

HostFault.AfterChartNeuronCommitBeforeUpstreamOutboxAcknowledgement exists only
in the test host configuration. The delivery dispatcher invokes ChartNeuron;
after ChartNeuron durably stores AddChartPoint, journals terminal
ChartPointAdded, and deduplicates the delivery, but before the originating
outbox marks that delivery acknowledged, it terminates the entire process.
This is unavailable to candidates, owner requests, and normal host
configuration.

HostFault.AfterGeneratedLocalOutboxCommitBeforeForwarderAcknowledgement is also
test-only. It terminates after ElonPostRuleNeuron durably commits the
ElonPostMatched envelope but before the dispatcher invokes
ChartForwarderNeuron or marks the local delivery acknowledged. The next
process is therefore the generated-synapse serialization and replay oracle.

- [ ] **Step 7: Write the Flutter integration assertion**

~~~dart
testWidgets('renders points created by the approved module', (tester) async {
  final host = await PocHostFixture.startApprovedElonCandidate();
  addTearDown(host.disposeAndVerifyDeleted);
  await host.fireTrustedSocialPost(author: 'elonmusk', postId: 'post-1');

  final projection = ChartProjectionHttpClient(
    baseUri: host.baseUri,
    ownerSessionToken: host.ownerSessionToken,
    chartId: 'elon-chart',
  );
  await tester.pumpWidget(ChartPocApp(projection: projection));
  await tester.pumpAndSettle();

  expect(find.byType(CustomPaint), findsOneWidget);
  expect(find.text('1'), findsOneWidget);
});
~~~

PocHostFixture is a Windows-desktop test utility. It owns one async
PocDataRoot lease per invocation and starts the already-built trusted POC host
as a child process only after the test control plane has quarantined, approved,
and promoted the candidate. The host boot enumerates only signed active
pointers; the fixture never passes a candidate DLL/path to it. It receives its
localhost base URI and an opaque test-owner session token over the trusted
test-scenario protocol, and fires the trusted social fixture.
disposeAndVerifyDeleted awaits child shutdown, disposes that lease, and fails
if the parent-store scan finds any artifact for its run ID. It does not fake
the chart projection or give a Flutter dependency or capability to the
candidate. The test runs on the Windows desktop target, where dart:io may
launch the local host process.

- [ ] **Step 8: Run final proof commands**

Run: dotnet build poc/DigitalBrain.Poc.slnx -c Release

Expected: 0 errors.

Run: dotnet test poc/DigitalBrain.Poc.slnx -c Release

Expected: PASS.

Run: cd poc/flutter/chart_poc; dart analyze; flutter test; flutter test integration_test -d windows

Expected: 0 analyzer errors and PASS.

## Plan self-review

| Approved requirement | Plan task |
| --- | --- |
| Fresh POC with no legacy reuse | Task 1 |
| One C# candidate, no generated project | Tasks 2 and 6 |
| Ordinary durable generated neurons | Tasks 2, 3, and 5 |
| Local generated synapse and two generated neurons | Tasks 5 and 6 |
| Trusted ChartNeuron and Flutter outside source | Tasks 4 and 8 |
| Roslyn AST, not raw LLM C# | Task 5 |
| Restricted capability surface | Task 5 |
| Runtime-owned envelope, route binding, and activation adapter | Task 3 |
| Journal, outbox, hard restart | Tasks 3 and 8 |
| Signed attestation, owner approval, promotion, rollback | Tasks 6 and 7 |
| Quiesced two-phase handoff and replay-resistant pointer head | Task 7 |
| Owner/family isolation and unforgeable projection access | Tasks 3, 4, 7, and 8 |
| Restart-only admission | Tasks 2 and 7 |
| Disposable POC owner data and teardown deletion proof | Task 3 |

There are no placeholder tasks. Each task names files, interfaces, a failing
proof, the minimum implementation boundary, a command, and an expected result.
The names are consistent throughout: SocialPostObserved → ElonPostMatched →
AddChartPoint → ChartPointAdded.

## Execution handoff

This plan is not being executed in the current documentation review. Before
the first code change, create an isolated worktree, refresh library facts, and
start Task 1 test-first. Execute one task at a time with a review checkpoint
after each task. Do not commit unless the owner separately authorizes it.
