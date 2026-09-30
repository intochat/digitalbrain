namespace IntoChat.Tests.E2E;

// The standard product composition, booted once for facts that only read or add per-fact scoped state.
public sealed class IntoChatHostFixture : SharedBrainFixture
{
    protected override Task<E2EBrain> StartHostAsync(CancellationToken cancellationToken)
        => IntoChatE2ETest.StartAsync(cancellationToken);
}

[CollectionDefinition(Name)]
public sealed class IntoChatHostCollection : ICollectionFixture<IntoChatHostFixture>
{
    public const string Name = "intochat-standard-host";
}
