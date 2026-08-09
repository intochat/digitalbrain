using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Runtime;
using Xunit;

namespace DigitalBrain.Poc.Runtime.Tests;

public sealed class CandidateCatalogFacts : IAsyncLifetime
{
    private readonly PocDataRoot _root = PocDataRoot.Create(TestPocRoot.Find());
    private readonly ECDsa _attestationKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _approvalKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _pointerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    [Fact]
    public async Task OnlyBoundOwnerCanApproveExactAttestedRecord()
    {
        var infrastructure = CreateInfrastructure();
        var attestation = CreateAttestation(infrastructure.Attestations, 'a', "owner-a");
        await infrastructure.Store.WriteAttestationAsync(attestation, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AuthorizationException>(() => infrastructure.Catalog.ApproveAsync(
            new AuthenticatedPrincipal("owner-b"),
            attestation.Payload.CandidateId,
            TestContext.Current.CancellationToken));

        await infrastructure.Catalog.ApproveAsync(
            new AuthenticatedPrincipal("owner-a"),
            attestation.Payload.CandidateId,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            CandidateLifecycle.ApprovedInactive,
            await infrastructure.Catalog.StatusAsync(
                attestation.Payload.CandidateId,
                TestContext.Current.CancellationToken));
        Assert.True(infrastructure.Approvals.Verify(await infrastructure.Store.ReadApprovalAsync(
            attestation.Payload.CandidateId,
            TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task ApprovalAuthorityComesFromSignedAttestationInsteadOfCandidateMirror()
    {
        var infrastructure = CreateInfrastructure();
        var attestation = CreateAttestation(infrastructure.Attestations, 'a', "owner-a");
        await infrastructure.Store.WriteAttestationAsync(attestation, TestContext.Current.CancellationToken);
        var candidateDirectory = Path.Combine(_root.CandidateRoot, attestation.Payload.CandidateId);
        Directory.CreateDirectory(candidateDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(candidateDirectory, "candidate.json"),
            "{\"ownerId\":\"owner-b\",\"familyId\":\"cf_bbbbbbbbbbbbbbbbbbbbbbbbbb\"}",
            TestContext.Current.CancellationToken);

        await infrastructure.Catalog.ApproveAsync(
            new AuthenticatedPrincipal("owner-a"),
            attestation.Payload.CandidateId,
            TestContext.Current.CancellationToken);

        var approval = await infrastructure.Store.ReadApprovalAsync(
            attestation.Payload.CandidateId,
            TestContext.Current.CancellationToken);
        Assert.Equal("owner-a", approval.Payload.OwnerId);
        Assert.Equal("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa", approval.Payload.FamilyId);
    }

    [Fact]
    public async Task PointerHeadCompareAndSwapLetsOnlyOneSameVersionAdvanceAcrossStoreInstances()
    {
        var infrastructure = CreateInfrastructure();
        var owner = new AuthenticatedPrincipal("owner-a");
        var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
        var empty = CandidatePointerHead.Empty(owner.OwnerId, family.Value);
        var first = infrastructure.Pointers.Sign(ActiveCandidatePointer.Next(empty, new string('a', 64)));
        var second = infrastructure.Pointers.Sign(ActiveCandidatePointer.Next(empty, new string('b', 64)));
        var otherStore = new TrustedCandidateCatalogStore(
            _root,
            infrastructure.Attestations,
            infrastructure.Approvals,
            infrastructure.Pointers);

        var results = await Task.WhenAll(
            infrastructure.Store.TryAdvancePointerHeadAsync(
                empty,
                first,
                TestContext.Current.CancellationToken),
            otherStore.TryAdvancePointerHeadAsync(
                empty,
                second,
                TestContext.Current.CancellationToken));

        Assert.Single(results, result => result.Succeeded);
        var updated = await infrastructure.Store.ReadPointerHeadAsync(
            owner,
            family,
            TestContext.Current.CancellationToken);
        Assert.Equal(1, updated.Version);
        Assert.True(
            updated.CurrentPayloadHash == first.PayloadHash ||
            updated.CurrentPayloadHash == second.PayloadHash);
    }

    [Fact]
    public async Task PointerIsVerifiedBeforeHeadComparisonAndReplayIsRejected()
    {
        var infrastructure = CreateInfrastructure();
        var owner = new AuthenticatedPrincipal("owner-a");
        var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
        var empty = CandidatePointerHead.Empty(owner.OwnerId, family.Value);
        var first = infrastructure.Pointers.Sign(ActiveCandidatePointer.Next(empty, new string('a', 64)));
        Assert.True((await infrastructure.Store.TryAdvancePointerHeadAsync(
            empty,
            first,
            TestContext.Current.CancellationToken)).Succeeded);
        var firstHead = await infrastructure.Store.ReadPointerHeadAsync(
            owner,
            family,
            TestContext.Current.CancellationToken);
        var second = infrastructure.Pointers.Sign(ActiveCandidatePointer.Next(firstHead, new string('b', 64)));
        Assert.True((await infrastructure.Store.TryAdvancePointerHeadAsync(
            firstHead,
            second,
            TestContext.Current.CancellationToken)).Succeeded);

        await infrastructure.Store.ReplacePointerFileForTestAsync(
            owner,
            family,
            first,
            TestContext.Current.CancellationToken);
        var replay = await infrastructure.Store.VerifyActivePointerAsync(
            owner,
            family,
            TestContext.Current.CancellationToken);
        Assert.False(replay.Succeeded);
        Assert.Equal(PointerVerificationFailure.StaleOrReplayed, replay.Failure);

        await infrastructure.Store.ReplacePointerFileForTestAsync(
            owner,
            family,
            second with { Signature = "corrupt" },
            TestContext.Current.CancellationToken);
        var invalid = await infrastructure.Store.VerifyActivePointerAsync(
            owner,
            family,
            TestContext.Current.CancellationToken);
        Assert.False(invalid.Succeeded);
        Assert.Equal(PointerVerificationFailure.InvalidSignature, invalid.Failure);
    }

    [Fact]
    public void ApprovalAndPointerSignersRequireNistP256AndFailClosedForMalformedValues()
    {
        var infrastructure = CreateInfrastructure();
        var family = "cf_aaaaaaaaaaaaaaaaaaaaaaaaaa";
        var hashA = new string('a', 64);
        var hashB = new string('b', 64);
        var payload = new OwnerApprovalPayload(
            hashA,
            _root.RunId,
            "owner-a",
            family,
            hashA,
            hashB,
            new string('c', 64),
            new string('d', 64));
        var approval = infrastructure.Approvals.Sign(payload);
        var pointer = infrastructure.Pointers.Sign(ActiveCandidatePointer.Next(
            CandidatePointerHead.Empty("owner-a", family),
            hashA));

        using var secp256k1 = ECDsa.Create(ECCurve.CreateFromValue("1.3.132.0.10"));
        var alternateCurveKey = secp256k1.ExportSubjectPublicKeyInfo();
        Assert.Throws<CryptographicException>(() => new OwnerApprovalSigner(
            _ => Array.Empty<byte>(),
            alternateCurveKey));
        Assert.Throws<CryptographicException>(() => new PointerSigner(
            _ => Array.Empty<byte>(),
            alternateCurveKey));
        Assert.Throws<ArgumentNullException>(() => new OwnerApprovalSigner(
            _ => Array.Empty<byte>(),
            null!));
        Assert.Throws<ArgumentNullException>(() => new PointerSigner(
            _ => Array.Empty<byte>(),
            null!));
        Assert.Throws<CryptographicException>(() => new OwnerApprovalSigner(
            _ => Array.Empty<byte>(),
            [0x30, 0x01, 0x00]));
        Assert.Throws<CryptographicException>(() => new PointerSigner(
            _ => Array.Empty<byte>(),
            [0x30, 0x01, 0x00]));

        Assert.False(infrastructure.Approvals.Verify(approval with { Signature = "not-base64" }));
        Assert.False(infrastructure.Pointers.Verify(pointer with { Signature = "not-base64" }));
        Assert.False(infrastructure.Pointers.Verify(pointer with
        {
            Signature = Convert.ToBase64String(new byte[63]),
        }));
        Assert.Throws<CryptographicException>(() => infrastructure.Approvals.Sign(payload with
        {
            SourceHash = new string('A', 64),
        }));
        Assert.Throws<CryptographicException>(() => infrastructure.Pointers.Sign(pointer with
        {
            FamilyId = "not-a-family",
        }));
        Assert.Throws<OverflowException>(() => ActiveCandidatePointer.Next(
            CandidatePointerHead.Empty("owner-a", family) with { Version = long.MaxValue },
            hashB));
    }

    [Fact]
    public async Task MissingPointerAcceptsOnlyCanonicalEmptyHeadAndNeverSignsPlantedLineage()
    {
        var infrastructure = CreateInfrastructure();
        var owner = new AuthenticatedPrincipal("owner-a");
        var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
        var planted = new CandidatePointerHead(
            owner.OwnerId,
            family.Value,
            new string('a', 64),
            new string('b', 64),
            new string('c', 64),
            new string('d', 64),
            41);
        var directory = Path.Combine(
            _root.ControlPlaneRoot,
            "pointers",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner.OwnerId))).ToLowerInvariant(),
            family.Value);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "head.json"),
            JsonSerializer.Serialize(
                planted,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            TestContext.Current.CancellationToken);

        var verified = await infrastructure.Store.VerifyActivePointerAsync(
            owner,
            family,
            TestContext.Current.CancellationToken);
        Assert.False(verified.Succeeded);
        Assert.Equal(PointerVerificationFailure.Malformed, verified.Failure);
        await Assert.ThrowsAsync<CryptographicException>(() => infrastructure.Store.ReadPointerHeadAsync(
            owner,
            family,
            TestContext.Current.CancellationToken));

        var proposed = infrastructure.Pointers.Sign(ActiveCandidatePointer.Next(
            planted,
            new string('e', 64)));
        var advanced = await infrastructure.Store.TryAdvancePointerHeadAsync(
            planted,
            proposed,
            TestContext.Current.CancellationToken);
        Assert.False(advanced.Succeeded);
    }

    [Fact]
    public async Task PointerReadersWaitForACompletedCrossProcessHeadPublication()
    {
        var pointerWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publicationCount = 0;
        var infrastructure = CreateInfrastructure(async cancellationToken =>
        {
            if (Interlocked.Increment(ref publicationCount) != 2)
            {
                return;
            }

            pointerWritten.TrySetResult();
            await releaseHead.Task.WaitAsync(cancellationToken);
        });
        var owner = new AuthenticatedPrincipal("owner-a");
        var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
        var empty = CandidatePointerHead.Empty(owner.OwnerId, family.Value);
        var first = infrastructure.Pointers.Sign(ActiveCandidatePointer.Next(empty, new string('a', 64)));
        Assert.True((await infrastructure.Store.TryAdvancePointerHeadAsync(
            empty,
            first,
            TestContext.Current.CancellationToken)).Succeeded);

        var firstHead = await infrastructure.Store.ReadPointerHeadAsync(
            owner,
            family,
            TestContext.Current.CancellationToken);
        var second = infrastructure.Pointers.Sign(ActiveCandidatePointer.Next(
            firstHead,
            new string('b', 64)));
        var advance = infrastructure.Store.TryAdvancePointerHeadAsync(
            firstHead,
            second,
            TestContext.Current.CancellationToken);
        await pointerWritten.Task.WaitAsync(TestContext.Current.CancellationToken);

        var reader = infrastructure.Store.VerifyActivePointerAsync(
            owner,
            family,
            TestContext.Current.CancellationToken);
        Assert.False(reader.IsCompleted);
        releaseHead.TrySetResult();

        Assert.True((await advance).Succeeded);
        var verified = await reader;
        Assert.True(verified.Succeeded);
        Assert.Equal(second.PayloadHash, verified.Pointer!.PayloadHash);
    }

    [Fact]
    public async Task PointerReadersHoldOneLinearizableLedgerAndHeadSnapshot()
    {
        var readerSawLedger = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pauseReader = 0;
        var infrastructure = CreateInfrastructure(
            afterPointerLedgerRead: async cancellationToken =>
            {
                if (Volatile.Read(ref pauseReader) == 0)
                {
                    return;
                }

                readerSawLedger.TrySetResult();
                await releaseReader.Task.WaitAsync(cancellationToken);
            });
        var owner = new AuthenticatedPrincipal("owner-a");
        var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
        var empty = CandidatePointerHead.Empty(owner.OwnerId, family.Value);
        var first = infrastructure.Pointers.Sign(ActiveCandidatePointer.Next(empty, new string('a', 64)));
        Assert.True((await infrastructure.Store.TryAdvancePointerHeadAsync(
            empty,
            first,
            TestContext.Current.CancellationToken)).Succeeded);
        var firstHead = await infrastructure.Store.ReadPointerHeadAsync(
            owner,
            family,
            TestContext.Current.CancellationToken);
        var second = infrastructure.Pointers.Sign(ActiveCandidatePointer.Next(
            firstHead,
            new string('b', 64)));
        var otherStore = new TrustedCandidateCatalogStore(
            _root,
            infrastructure.Attestations,
            infrastructure.Approvals,
            infrastructure.Pointers);

        Volatile.Write(ref pauseReader, 1);
        var reader = infrastructure.Store.VerifyActivePointerAsync(
            owner,
            family,
            TestContext.Current.CancellationToken);
        await readerSawLedger.Task.WaitAsync(TestContext.Current.CancellationToken);

        var writer = otherStore.TryAdvancePointerHeadAsync(
            firstHead,
            second,
            TestContext.Current.CancellationToken);
        Assert.False(writer.IsCompleted);

        releaseReader.TrySetResult();
        var verified = await reader;
        Assert.True(verified.Succeeded);
        Assert.Equal(first.PayloadHash, verified.Pointer!.PayloadHash);
        Assert.True((await writer).Succeeded);

        var final = await infrastructure.Store.VerifyActivePointerAsync(
            owner,
            family,
            TestContext.Current.CancellationToken);
        Assert.True(final.Succeeded);
        Assert.Equal(second.PayloadHash, final.Pointer!.PayloadHash);
    }

    [Fact]
    public async Task HistoricalSignedPointerChainKeepsOlderDisplacedCandidatesRolledBack()
    {
        var infrastructure = CreateInfrastructure();
        var owner = new AuthenticatedPrincipal("owner-a");
        var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
        var candidateA = CreateAttestation(infrastructure.Attestations, 'a', owner.OwnerId);
        var candidateB = CreateAttestation(infrastructure.Attestations, 'b', owner.OwnerId);
        var candidateC = CreateAttestation(infrastructure.Attestations, 'c', owner.OwnerId);
        foreach (var attestation in new[] { candidateA, candidateB, candidateC })
        {
            await infrastructure.Store.WriteAttestationAsync(
                attestation,
                TestContext.Current.CancellationToken);
            await infrastructure.Catalog.ApproveAsync(
                owner,
                attestation.Payload.CandidateId,
                TestContext.Current.CancellationToken);
        }

        var empty = CandidatePointerHead.Empty(owner.OwnerId, family.Value);
        var pointerA = infrastructure.Pointers.Sign(ActiveCandidatePointer.Next(
            empty,
            candidateA.Payload.SourceHash));
        Assert.True((await infrastructure.Store.TryAdvancePointerHeadAsync(
            empty,
            pointerA,
            TestContext.Current.CancellationToken)).Succeeded);
        var headA = await infrastructure.Store.ReadPointerHeadAsync(
            owner,
            family,
            TestContext.Current.CancellationToken);
        var pointerB = infrastructure.Pointers.Sign(ActiveCandidatePointer.Next(
            headA,
            candidateB.Payload.SourceHash));
        Assert.True((await infrastructure.Store.TryAdvancePointerHeadAsync(
            headA,
            pointerB,
            TestContext.Current.CancellationToken)).Succeeded);
        var headB = await infrastructure.Store.ReadPointerHeadAsync(
            owner,
            family,
            TestContext.Current.CancellationToken);
        var pointerC = infrastructure.Pointers.Sign(ActiveCandidatePointer.Next(
            headB,
            candidateC.Payload.SourceHash));
        Assert.True((await infrastructure.Store.TryAdvancePointerHeadAsync(
            headB,
            pointerC,
            TestContext.Current.CancellationToken)).Succeeded);

        Assert.Equal(
            CandidateLifecycle.RolledBack,
            await infrastructure.Catalog.StatusAsync(
                candidateA.Payload.CandidateId,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            CandidateLifecycle.RolledBack,
            await infrastructure.Catalog.StatusAsync(
                candidateB.Payload.CandidateId,
                TestContext.Current.CancellationToken));

        var history = Path.Combine(
            _root.ControlPlaneRoot,
            "pointers",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner.OwnerId))).ToLowerInvariant(),
            family.Value,
            "history",
            pointerB.PayloadHash + ".json");
        File.SetAttributes(history, File.GetAttributes(history) & ~FileAttributes.ReadOnly);
        File.Delete(history);
        await Assert.ThrowsAsync<CryptographicException>(() => infrastructure.Catalog.StatusAsync(
            candidateA.Payload.CandidateId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FirstPublishedPointerRecoversThroughAnAuthenticatedEmptyLineage()
    {
        var infrastructure = CreateInfrastructure();
        var owner = new AuthenticatedPrincipal("owner-a");
        var family = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
        var empty = CandidatePointerHead.Empty(owner.OwnerId, family.Value);
        var proposed = infrastructure.Pointers.Sign(ActiveCandidatePointer.Next(
            empty,
            new string('a', 64)));
        Assert.True((await infrastructure.Store.TryAdvancePointerHeadAsync(
            empty,
            proposed,
            TestContext.Current.CancellationToken)).Succeeded);

        Assert.True(await infrastructure.Store.TryRestoreCanonicalEmptyPointerHeadAsync(
            proposed,
            TestContext.Current.CancellationToken));
        var verified = await infrastructure.Store.VerifyActivePointerAsync(
            owner,
            family,
            TestContext.Current.CancellationToken);
        Assert.False(verified.Succeeded);
        Assert.Equal(PointerVerificationFailure.Missing, verified.Failure);
        var recoveredHead = await infrastructure.Store.ReadPointerHeadAsync(
            owner,
            family,
            TestContext.Current.CancellationToken);
        Assert.Equal(2, recoveredHead.Version);
        Assert.Equal(new string('0', 64), recoveredHead.CurrentCandidateSourceHash);
        Assert.Equal(proposed.CandidateSourceHash, recoveredHead.PreviousCandidateSourceHash);
        Assert.Equal(proposed.PayloadHash, recoveredHead.ParentPayloadHash);

        var retry = infrastructure.Pointers.Sign(ActiveCandidatePointer.Next(
            recoveredHead,
            new string('b', 64)));
        Assert.True((await infrastructure.Store.TryAdvancePointerHeadAsync(
            recoveredHead,
            retry,
            TestContext.Current.CancellationToken)).Succeeded);
        Assert.Equal(
            retry.PayloadHash,
            (await infrastructure.Store.VerifyActivePointerAsync(
                owner,
                family,
                TestContext.Current.CancellationToken)).Pointer!.PayloadHash);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _attestationKey.Dispose();
        _approvalKey.Dispose();
        _pointerKey.Dispose();
        await _root.DisposeAsync();
    }

    private Infrastructure CreateInfrastructure(
        Func<CancellationToken, Task>? beforePointerHeadCommit = null,
        Func<CancellationToken, Task>? afterPointerLedgerRead = null)
    {
        var attestations = new AttestationSigner(
            payload => _attestationKey.SignData(
                payload,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            _attestationKey.ExportSubjectPublicKeyInfo());
        var approvals = new OwnerApprovalSigner(
            payload => _approvalKey.SignData(
                payload,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            _approvalKey.ExportSubjectPublicKeyInfo());
        var pointers = new PointerSigner(
            payload => _pointerKey.SignData(
                payload,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            _pointerKey.ExportSubjectPublicKeyInfo());
        var store = new TrustedCandidateCatalogStore(
            _root,
            attestations,
            approvals,
            pointers,
            beforePointerHeadCommit,
            afterPointerLedgerRead);
        return new Infrastructure(
            attestations,
            approvals,
            pointers,
            store,
            new CandidateCatalog(store));
    }

    private CandidateAttestation CreateAttestation(
        AttestationSigner signer,
        char candidateCharacter,
        string ownerId)
    {
        var candidateId = new string(candidateCharacter, 64);
        return signer.Sign(new CandidateAttestationPayload(
            candidateId,
            _root.RunId,
            ownerId,
            "cf_aaaaaaaaaaaaaaaaaaaaaaaaaa",
            candidateId,
            new string('b', 64),
            new string('c', 64),
            new string('d', 64))
        {
            Revision = $"quarantine-{new string('b', 64)}",
            Status = "awaitingOwnerApproval",
            SourcePath = "elon-chart.cs",
            AssemblyPath = "module.dll",
            GrantedInputAliases = ["db.poc.social.post-observed.v1"],
            GrantedCandidateOutputAliases =
                ["db.poc.family.cf_aaaaaaaaaaaaaaaaaaaaaaaaaa.matched.v1"],
            GrantedTrustedOutputAliases = ["db.poc.chart.add-point.v1"],
            GrantedTargetScopes = ["elon-chart"],
            ResolvedReferences = ["assembly|DigitalBrain.Poc.Abstractions"],
            NormalizedAstHash = new string('1', 64),
            FixedHeaderHash = new string('2', 64),
            CompilerHash = new string('3', 64),
            SdkHash = new string('4', 64),
            ReferencesHash = new string('5', 64),
            CapabilitiesHash = new string('6', 64),
            ContractsHash = new string('7', 64),
            StateSchemaHash = new string('8', 64),
        });
    }

    private sealed record Infrastructure(
        AttestationSigner Attestations,
        OwnerApprovalSigner Approvals,
        PointerSigner Pointers,
        TrustedCandidateCatalogStore Store,
        CandidateCatalog Catalog);
}
