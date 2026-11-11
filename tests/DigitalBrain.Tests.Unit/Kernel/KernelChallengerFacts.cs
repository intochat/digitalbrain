#pragma warning disable CA2201
#pragma warning disable CA1822
#pragma warning disable CA1040
using DigitalBrain.Contracts;
using DigitalBrain.Neurons;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DigitalBrain.Access;
using DigitalBrain.Invocation;
using DigitalBrain.Kernel;

namespace DigitalBrain.Tests.Unit.Kernel;

/// <summary>
/// Empirical adversarial challenge suite for Milestone 2:
/// Pure Kernel Rules & Static Call Catalog (R02, R06).
/// Tests stress scenarios, edge cases, exception safety, and collectible ALC unloading.
/// </summary>
public sealed class KernelChallengerFacts
{
    // =========================================================================
    // 1. Interleaving Predicate Safety & Exception Stress
    // =========================================================================

    public interface IChallengeContract
    {
        Task TurnCall();
        [Interleaves]
        Task InterleavedCall();
        [ReadOnlyCall]
        Task ReadOnlyCall();
    }

    public class BaseNeuronLevel1
    {
        // Deep private static predicate 2 levels up
        private static bool GrandparentPredicate(RuntimeInvocation call) => call.Method == "DeepApproved";
    }

    public class BaseNeuronLevel2 : BaseNeuronLevel1;

    [InterleavesWhen("GrandparentPredicate")]
    public class DerivedNeuronWithDeepPredicate : BaseNeuronLevel2, IChallengeContract
    {
        public Task TurnCall() => Task.CompletedTask;
        public Task InterleavedCall() => Task.CompletedTask;
        public Task ReadOnlyCall() => Task.CompletedTask;
    }

    public class DiverseExceptionsNeuron : IChallengeContract
    {
        public static Exception? ExceptionToThrow { get; set; }

        public static bool DynamicPredicate(RuntimeInvocation call)
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return call.Method == "Allowed";
        }

        public Task TurnCall() => Task.CompletedTask;
        public Task InterleavedCall() => Task.CompletedTask;
        public Task ReadOnlyCall() => Task.CompletedTask;
    }

    [InterleavesWhen(nameof(DiverseExceptionsNeuron.DynamicPredicate))]
    public class ConfiguredDiverseNeuron : DiverseExceptionsNeuron;

    private static RuntimeInvocation MakeInvocation(string method)
        => new("behavior", typeof(IChallengeContract).FullName!, "b1/type/k1", method, []);

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentNullException))]
    [InlineData(typeof(DivideByZeroException))]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(IndexOutOfRangeException))]
    [InlineData(typeof(FormatException))]
    public void MayInterleave_catches_all_non_fatal_exceptions_and_returns_false(Type exceptionType)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType)!;
        DiverseExceptionsNeuron.ExceptionToThrow = ex;

        var method = typeof(IChallengeContract).GetMethod(nameof(IChallengeContract.TurnCall))!;
        var call = MakeInvocation("AnyMethod");

        // 1. Overload with MethodInfo
        var resultMethod = CallPolicy.MayInterleave(typeof(ConfiguredDiverseNeuron), method, call);
        Assert.False(resultMethod);

        // 2. Overload with CallDescriptor
        var catalog = CallCatalog.Build(typeof(IChallengeContract));
        var desc = catalog.Resolve(nameof(IChallengeContract.TurnCall), arity: 0);
        var resultDesc = CallPolicy.MayInterleave(typeof(ConfiguredDiverseNeuron), desc, call);
        Assert.False(resultDesc);

        // 3. Overload with descriptor where Method is null
        var descNullMethod = new CallDescriptor(nameof(IChallengeContract.TurnCall), 0, typeof(Task), (t, a) => Task.FromResult<object?>(null))
        {
            Scheduling = CallScheduling.Conditional,
        };
        var resultDescNullMethod = CallPolicy.MayInterleave(typeof(ConfiguredDiverseNeuron), descNullMethod, call);
        Assert.False(resultDescNullMethod);

        // Reset
        DiverseExceptionsNeuron.ExceptionToThrow = null;
    }

    [Fact]
    public void MayInterleave_rethrows_fatal_OutOfMemoryException_across_all_overloads()
    {
        DiverseExceptionsNeuron.ExceptionToThrow = new OutOfMemoryException("Fatal uncatchable memory exhaustion");

        var method = typeof(IChallengeContract).GetMethod(nameof(IChallengeContract.TurnCall))!;
        var call = MakeInvocation("AnyMethod");

        // 1. MethodInfo overload
        Assert.Throws<OutOfMemoryException>(() =>
            CallPolicy.MayInterleave(typeof(ConfiguredDiverseNeuron), method, call));

        // 2. CallDescriptor overload
        var catalog = CallCatalog.Build(typeof(IChallengeContract));
        var desc = catalog.Resolve(nameof(IChallengeContract.TurnCall), arity: 0);
        Assert.Throws<OutOfMemoryException>(() =>
            CallPolicy.MayInterleave(typeof(ConfiguredDiverseNeuron), desc, call));

        // 3. CallDescriptor without MethodInfo
        var descNullMethod = new CallDescriptor(nameof(IChallengeContract.TurnCall), 0, typeof(Task), (t, a) => Task.FromResult<object?>(null))
        {
            Scheduling = CallScheduling.Conditional,
        };
        Assert.Throws<OutOfMemoryException>(() =>
            CallPolicy.MayInterleave(typeof(ConfiguredDiverseNeuron), descNullMethod, call));

        // Reset
        DiverseExceptionsNeuron.ExceptionToThrow = null;
    }

    [Fact]
    public void InterleavesWhen_searches_entire_inheritance_chain_for_private_static_predicate()
    {
        var method = typeof(IChallengeContract).GetMethod(nameof(IChallengeContract.TurnCall))!;
        var approvedCall = MakeInvocation("DeepApproved");
        var rejectedCall = MakeInvocation("OtherMethod");

        Assert.True(CallPolicy.MayInterleave(typeof(DerivedNeuronWithDeepPredicate), method, approvedCall));
        Assert.False(CallPolicy.MayInterleave(typeof(DerivedNeuronWithDeepPredicate), method, rejectedCall));
    }

    // =========================================================================
    // 2. Reaction Ordering, Alias Resolution & Wants Stress
    // =========================================================================

    public record RootSignal : Signal;
    public record ChildSignal : RootSignal;
    public record GrandchildSignal : ChildSignal;

    [Signal("alpha.v1")]
    public record AlphaSignal : Signal;

    [Signal("beta.v1")]
    public record BetaSignal : Signal;

    public class HierarchyReactionNeuron : Neuron
    {
        public string LastHandledBy { get; private set; } = string.Empty;

        [On]
        public void HandleRoot(RootSignal s) => LastHandledBy = "Root";

        [On]
        public void HandleChild(ChildSignal s) => LastHandledBy = "Child";

        [On]
        public void HandleGrandchild(GrandchildSignal s) => LastHandledBy = "Grandchild";
    }

    public class BaseShadowNeuron : Neuron
    {
        public bool BaseExecuted { get; set; }

        [On]
        public void OnEvent(AlphaSignal s) => BaseExecuted = true;
    }

    public class DerivedShadowNeuron : BaseShadowNeuron
    {
        public bool DerivedExecuted { get; set; }

        [On]
        public new void OnEvent(AlphaSignal s) => DerivedExecuted = true;
    }

    [Fact]
    public async Task ReactionTable_dispatches_to_most_derived_signal_parameter_type()
    {
        var neuron = new HierarchyReactionNeuron();

        // 1. Root signal
        await ReactionTable.React(neuron, new RootSignal(), CancellationToken.None);
        Assert.Equal("Root", neuron.LastHandledBy);

        // 2. Child signal
        await ReactionTable.React(neuron, new ChildSignal(), CancellationToken.None);
        Assert.Equal("Child", neuron.LastHandledBy);

        // 3. Grandchild signal
        await ReactionTable.React(neuron, new GrandchildSignal(), CancellationToken.None);
        Assert.Equal("Grandchild", neuron.LastHandledBy);
    }

    [Fact]
    public void ReactionTable_deterministically_favors_derived_class_over_base_class_for_identical_signals()
    {
        // When both base and derived declare an On method for AlphaSignal with same signature:
        // ReactionTable must deterministically pick the derived class handler
        var method = ReactionTable.Find(typeof(DerivedShadowNeuron), typeof(AlphaSignal));
        Assert.NotNull(method);
        Assert.Equal(typeof(DerivedShadowNeuron), method.DeclaringType);
    }

    [Fact]
    public void Wants_handles_mixed_exact_alias_and_fallback_scenarios()
    {
        var alpha = new AlphaSignal();
        var beta = new BetaSignal();

        // Wants with empty filters -> false
        Assert.False(ReactionTable.Wants([], [], alpha));

        // Wants with exact type
        Assert.True(ReactionTable.Wants([typeof(AlphaSignal)], [], alpha));
        Assert.False(ReactionTable.Wants([typeof(BetaSignal)], [], alpha));

        // Wants with string alias
        Assert.True(ReactionTable.Wants([], ["alpha.v1"], alpha));
        Assert.False(ReactionTable.Wants([], ["alpha.v2"], alpha));

        // Wants with type matching same alias
        Assert.True(ReactionTable.Wants([typeof(AlphaSignal)], ["nonexistent"], alpha));

        // Subscribing to base type
        Assert.True(ReactionTable.Wants([typeof(RootSignal)], [], new GrandchildSignal()));
    }

    [Fact]
    public void SortSynapses_preserves_deterministic_order_under_permutation()
    {
        var records = new[]
        {
            ("srv-b", "sub-1", "sig.c"),
            ("srv-a", "sub-2", "sig.a"),
            ("srv-a", "sub-1", "sig.b"),
            ("srv-a", "sub-1", "sig.a"),
        };

        var sorted = ReactionTable.SortSynapses(records, r => r.Item1, r => r.Item2, r => r.Item3);

        Assert.Equal(("srv-a", "sub-1", "sig.a"), sorted[0]);
        Assert.Equal(("srv-a", "sub-1", "sig.b"), sorted[1]);
        Assert.Equal(("srv-a", "sub-2", "sig.a"), sorted[2]);
        Assert.Equal(("srv-b", "sub-1", "sig.c"), sorted[3]);
    }

    // =========================================================================
    // 3. Retention Math Boundary & Edge Case Stress
    // =========================================================================

    [Theory]
    [InlineData(0, 0, 0, null, 0)]            // Zero window, zero last, no live -> 0
    [InlineData(100, 0, 0, null, 100)]        // Zero window, last=100 -> 100
    [InlineData(100, 0, 0, 45L, 45)]          // Zero window, last=100, live=45 -> 45
    [InlineData(100, 20, 10, null, 80)]       // WindowSignals dominates: 100 - 20 = 80
    [InlineData(100, 10, 35, null, 65)]       // NewerThanAge dominates: 100 - 35 = 65
    [InlineData(100, 20, 10, 30L, 30)]        // Slowest subscriber pins floor to 30
    [InlineData(100, 20, 10, 95L, 80)]        // Subscriber ahead does not pin floor: bounded by window at 80
    [InlineData(10, 50, 0, null, 0)]          // Underflow: 10 - 50 = -40 -> max(0, -40) = 0
    [InlineData(-5, 10, 0, null, 0)]          // Negative last -> 0
    [InlineData(100, 20, 10, 0L, 0)]          // Subscriber at sequence 0 -> floor is 0
    public void RetentionMath_Floor_handles_all_boundary_cases(
        long last,
        int windowSignals,
        long newerThanAge,
        long? lowestLive,
        long expectedFloor)
    {
        var actual = RetentionMath.Floor(last, windowSignals, newerThanAge, lowestLive);
        Assert.Equal(expectedFloor, actual);
    }

    [Fact]
    public void RetentionMath_CompactionRange_and_IsRetained_edge_cases()
    {
        // 1. New floor <= current floor -> no compaction
        Assert.Null(RetentionMath.CompactionRange(100, 100));
        Assert.Null(RetentionMath.CompactionRange(100, 99));

        // 2. New floor > current floor -> contiguous range
        var range = RetentionMath.CompactionRange(100, 101);
        Assert.NotNull(range);
        Assert.Equal(101, range.Value.FromSequence);
        Assert.Equal(101, range.Value.ToSequence);

        var largerRange = RetentionMath.CompactionRange(0, 50);
        Assert.NotNull(largerRange);
        Assert.Equal(1, largerRange.Value.FromSequence);
        Assert.Equal(50, largerRange.Value.ToSequence);

        // 3. Retention boundary: entry at floor is trimmed (not retained), entry above floor is retained
        Assert.False(RetentionMath.IsRetained(entrySequence: 50, retentionFloor: 50));
        Assert.True(RetentionMath.IsRetained(entrySequence: 51, retentionFloor: 50));
        Assert.False(RetentionMath.IsRetained(entrySequence: 49, retentionFloor: 50));
    }

    // =========================================================================
    // 4. Zero Reflection Invoker Performance & Collectible ALC Safety
    // =========================================================================

    public interface IBenchmarkContract
    {
        int ComputeFast(int a, int b);
        Task<int> ComputeAsync(int a, int b);
    }

    public class BenchmarkNeuron : IBenchmarkContract
    {
        public int ComputeFast(int a, int b) => a + b;
        public Task<int> ComputeAsync(int a, int b) => Task.FromResult(a * b);
    }

    [Fact]
    public async Task Precompiled_invoker_executes_high_throughput_without_reflection_overhead()
    {
        var catalog = CallCatalog.Build(typeof(IBenchmarkContract));
        var neuron = new BenchmarkNeuron();
        var desc = catalog.Resolve(nameof(IBenchmarkContract.ComputeFast), arity: 2);

        // Warm up
        _ = await desc.Invoker(neuron, [1, 2]);

        const int iterations = 50_000;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var res = await desc.Invoker(neuron, [i, 1]);
            Assert.Equal(i + 1, res);
        }
        sw.Stop();

        // 50,000 compiled expression invocations should complete well under 500ms
        Assert.True(sw.ElapsedMilliseconds < 1500, $"Expected fast execution, took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Collectible_types_bypass_static_caches_and_prevent_memory_pinning()
    {
        var asmName = new AssemblyName("DynamicCollectibleAssembly");
        var asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.RunAndCollect);
        var modBuilder = asmBuilder.DefineDynamicModule("DynamicCollectibleModule");

        // Define interface contract
        var interfaceBuilder = modBuilder.DefineType("IDynamicContract", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        _ = interfaceBuilder.DefineMethod("RunDynamic", MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual, typeof(Task<int>), [typeof(int)]);
        var contractType = interfaceBuilder.CreateType()!;

        // Define implementation
        var classBuilder = modBuilder.DefineType("DynamicNeuron", TypeAttributes.Public | TypeAttributes.Class, typeof(Neuron), [contractType]);
        var implMethod = classBuilder.DefineMethod("RunDynamic", MethodAttributes.Public | MethodAttributes.Virtual, typeof(Task<int>), [typeof(int)]);
        var il = implMethod.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(typeof(int)));
        il.Emit(OpCodes.Ret);
        var neuronType = classBuilder.CreateType()!;

        // 1. Verify types are collectible
        Assert.True(contractType.IsCollectible, "Contract type must be collectible");
        Assert.True(neuronType.IsCollectible, "Neuron type must be collectible");
        Assert.True(contractType.Assembly.IsCollectible, "Assembly must be collectible");

        // 2. CallCatalog bypasses static cache for collectible contract
        var cat1 = CallCatalog.GetOrCreate(contractType);
        var cat2 = CallCatalog.GetOrCreate(contractType);
        Assert.NotNull(cat1);
        Assert.NotNull(cat2);
        Assert.NotSame(cat1, cat2); // Must NOT return cached singleton!

        // 3. CallCatalog bypasses static cache for collectible neuron
        var ncat1 = CallCatalog.GetOrCreateForNeuron(neuronType);
        var ncat2 = CallCatalog.GetOrCreateForNeuron(neuronType);
        Assert.NotNull(ncat1);
        Assert.NotNull(ncat2);
        Assert.NotSame(ncat1, ncat2); // Must NOT return cached singleton!

        // 4. CallPolicy.MayInterleave evaluates safely with collectible types
        var bakedMethod = neuronType.GetMethod("RunDynamic")!;
        var call = new RuntimeInvocation("b", contractType.FullName!, "key", "RunDynamic", []);
        var mayInterleave = CallPolicy.MayInterleave(neuronType, bakedMethod, call);
        Assert.False(mayInterleave);

        // 5. ReactionTable.Find evaluates safely without caching collectible types
        var reaction = ReactionTable.Find(neuronType, typeof(RootSignal));
        Assert.Null(reaction);
    }
}
