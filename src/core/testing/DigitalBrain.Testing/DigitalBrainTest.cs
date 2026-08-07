using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace DigitalBrain.Testing;

public abstract class DigitalBrainTest : IAsyncLifetime
{
    private const BindingFlags DeclaredCompose =
        BindingFlags.Instance
        | BindingFlags.NonPublic
        | BindingFlags.Public
        | BindingFlags.DeclaredOnly;

    private static readonly Type[] ComposeSignature = [typeof(DigitalBrainTestBuilder)];

    private static readonly MethodInfo ComposeSlot = typeof(DigitalBrainTest).GetMethod(
        nameof(Compose), DeclaredCompose, binder: null, ComposeSignature, modifiers: null)!;

    private static readonly ConcurrentDictionary<Type, Type> CompositionKeys = new();

    private TestBrain? _leased;

    protected static CancellationToken Cancellation
        => TestContext.Current.CancellationToken;

    protected virtual void Compose(DigitalBrainTestBuilder brain)
    {
    }

    protected async ValueTask<TestBrain> BrainAsync()
        => _leased ??= await BrainTestClusters.Registered.LeaseAsync(
            CompositionOf(GetType()),
            Compose,
            Cancellation).ConfigureAwait(false);

    public virtual ValueTask InitializeAsync()
        => ValueTask.CompletedTask;

    public virtual async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        var brain = _leased;
        _leased = null;
        if (brain is not null)
        {
            await brain.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static Type CompositionOf(Type test)
        => CompositionKeys.GetOrAdd(test, static candidate =>
        {
            for (var declaring = candidate; declaring is not null; declaring = declaring.BaseType)
            {
                var declared = declaring.GetMethod(
                    nameof(Compose), DeclaredCompose, binder: null, ComposeSignature, modifiers: null);
                if (declared is not null && declared.GetBaseDefinition().Equals(ComposeSlot))
                {
                    return declaring;
                }
            }

            throw new UnreachableException(
                $"{candidate} derives from {nameof(DigitalBrainTest)} but declares no composition slot.");
        });
}
