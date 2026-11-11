#pragma warning disable CA2201
using DigitalBrain.Contracts;
using DigitalBrain.Neurons;
using DigitalBrain.Access;
using DigitalBrain.Invocation;
using DigitalBrain.Kernel;

namespace DigitalBrain.Tests.Unit.Kernel;

public sealed class CallPolicyFacts
{
    public interface ITestContract
    {
        Task MutatingCall();

        [Interleaves]
        Task InterleavingCall();

        [ReadOnlyCall]
        Task ReadOnlyCall();

        [CallTimeout(350)]
        Task TimeoutCall();

        [Public]
        Task PublicCall();

        [Internal]
        Task InternalCall();

        [Shared]
        Task SharedCall();
    }

    [Public]
    public interface IPublicContract
    {
        Task DefaultCall();
    }

    internal interface IInternalContract
    {
        Task DefaultCall();
    }

    public class BaseNeuronWithPredicate
    {
        private static bool PrivateBasePredicate(RuntimeInvocation call) => call.Method == "BaseAllowed";
    }

    [InterleavesWhen("PrivateBasePredicate")]
    public class DerivedNeuron : BaseNeuronWithPredicate, ITestContract
    {
        public Task MutatingCall() => Task.CompletedTask;
        public Task InterleavingCall() => Task.CompletedTask;
        public Task ReadOnlyCall() => Task.CompletedTask;
        public Task TimeoutCall() => Task.CompletedTask;
        public Task PublicCall() => Task.CompletedTask;
        public Task InternalCall() => Task.CompletedTask;
        public Task SharedCall() => Task.CompletedTask;
    }

    public class ThrowingPredicateNeuron : ITestContract
    {
        public static bool ThrowingPredicate(RuntimeInvocation call) => throw new InvalidOperationException("Simulated predicate failure");

        public Task MutatingCall() => Task.CompletedTask;
        public Task InterleavingCall() => Task.CompletedTask;
        public Task ReadOnlyCall() => Task.CompletedTask;
        public Task TimeoutCall() => Task.CompletedTask;
        public Task PublicCall() => Task.CompletedTask;
        public Task InternalCall() => Task.CompletedTask;
        public Task SharedCall() => Task.CompletedTask;
    }

    [InterleavesWhen(nameof(ThrowingPredicateNeuron.ThrowingPredicate))]
    public class ConfiguredThrowingNeuron : ThrowingPredicateNeuron;

    public class OomPredicateNeuron
    {
        public static bool OomPredicate(RuntimeInvocation call) => throw new OutOfMemoryException("Fatal");
    }

    [InterleavesWhen(nameof(OomPredicateNeuron.OomPredicate))]
    public class ConfiguredOomNeuron : OomPredicateNeuron;

    [Reentrant]
    public class ReentrantNeuron : ITestContract
    {
        public Task MutatingCall() => Task.CompletedTask;
        public Task InterleavingCall() => Task.CompletedTask;
        public Task ReadOnlyCall() => Task.CompletedTask;
        public Task TimeoutCall() => Task.CompletedTask;
        public Task PublicCall() => Task.CompletedTask;
        public Task InternalCall() => Task.CompletedTask;
        public Task SharedCall() => Task.CompletedTask;
    }

    [InterleavesWhen("NoSuchMethod")]
    public class MisconfiguredNeuron;

    private static RuntimeInvocation Invocation(string method)
        => new("behavior", typeof(ITestContract).FullName!, "b1/type/k1", method, []);

    [Fact]
    public void Reentrant_class_interleaves_all_calls()
    {
        var method = typeof(ITestContract).GetMethod(nameof(ITestContract.MutatingCall))!;
        var call = Invocation(nameof(ITestContract.MutatingCall));

        Assert.True(CallPolicy.MayInterleave(typeof(ReentrantNeuron), method, call));
    }

    [Fact]
    public void Method_level_interleaving_and_readonly_attributes_permit_interleaving()
    {
        var interleavingMethod = typeof(ITestContract).GetMethod(nameof(ITestContract.InterleavingCall))!;
        var readOnlyMethod = typeof(ITestContract).GetMethod(nameof(ITestContract.ReadOnlyCall))!;
        var mutatingMethod = typeof(ITestContract).GetMethod(nameof(ITestContract.MutatingCall))!;

        var call = Invocation(nameof(ITestContract.InterleavingCall));

        Assert.True(CallPolicy.MayInterleave(typeof(DerivedNeuron), interleavingMethod, call));
        Assert.True(CallPolicy.MayInterleave(typeof(DerivedNeuron), readOnlyMethod, Invocation(nameof(ITestContract.ReadOnlyCall))));
        Assert.False(CallPolicy.MayInterleave(typeof(DerivedNeuron), mutatingMethod, Invocation(nameof(ITestContract.MutatingCall))));
    }

    [Fact]
    public void InterleavesWhen_predicate_on_base_class_with_private_visibility_is_discovered_and_evaluated()
    {
        var method = typeof(ITestContract).GetMethod(nameof(ITestContract.MutatingCall))!;
        var allowedCall = Invocation("BaseAllowed");
        var rejectedCall = Invocation("OtherMethod");

        Assert.True(CallPolicy.MayInterleave(typeof(DerivedNeuron), method, allowedCall));
        Assert.False(CallPolicy.MayInterleave(typeof(DerivedNeuron), method, rejectedCall));
    }

    [Fact]
    public void Throwing_predicate_does_not_crash_and_refuses_interleaving()
    {
        var method = typeof(ITestContract).GetMethod(nameof(ITestContract.MutatingCall))!;
        var call = Invocation("AnyCall");

        // Should return false rather than rethrowing
        Assert.False(CallPolicy.MayInterleave(typeof(ConfiguredThrowingNeuron), method, call));
    }

    [Fact]
    public void OutOfMemoryException_in_predicate_is_not_swallowed()
    {
        var method = typeof(ITestContract).GetMethod(nameof(ITestContract.MutatingCall))!;
        var call = Invocation("AnyCall");

        Assert.Throws<OutOfMemoryException>(() => CallPolicy.MayInterleave(typeof(ConfiguredOomNeuron), method, call));
    }

    [Fact]
    public void Misconfigured_predicate_fails_at_resolution_naming_class_and_predicate()
    {
        var error = Assert.Throws<InvalidOperationException>(() => CallPolicy.PredicateOf(typeof(MisconfiguredNeuron)));
        Assert.Contains(nameof(MisconfiguredNeuron), error.Message, StringComparison.Ordinal);
        Assert.Contains("NoSuchMethod", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TimeoutOf_reads_call_timeout_attribute_or_null()
    {
        var timeoutMethod = typeof(ITestContract).GetMethod(nameof(ITestContract.TimeoutCall))!;
        var regularMethod = typeof(ITestContract).GetMethod(nameof(ITestContract.MutatingCall))!;

        Assert.Equal(TimeSpan.FromMilliseconds(350), CallPolicy.TimeoutOf(timeoutMethod));
        Assert.Null(CallPolicy.TimeoutOf(regularMethod));
    }

    [Fact]
    public void AccessOf_classifies_operation_access_with_attribute_precedence()
    {
        var publicMethod = typeof(ITestContract).GetMethod(nameof(ITestContract.PublicCall))!;
        var internalMethod = typeof(ITestContract).GetMethod(nameof(ITestContract.InternalCall))!;
        var sharedMethod = typeof(ITestContract).GetMethod(nameof(ITestContract.SharedCall))!;
        var defaultMethod = typeof(ITestContract).GetMethod(nameof(ITestContract.MutatingCall))!;

        Assert.Equal(OperationAccess.Public, CallPolicy.AccessOf(typeof(ITestContract), publicMethod));
        Assert.Equal(OperationAccess.Internal, CallPolicy.AccessOf(typeof(ITestContract), internalMethod));
        Assert.Equal(OperationAccess.Shared, CallPolicy.AccessOf(typeof(ITestContract), sharedMethod));
        Assert.Equal(OperationAccess.Owner, CallPolicy.AccessOf(typeof(ITestContract), defaultMethod));

        var publicContractMethod = typeof(IPublicContract).GetMethod(nameof(IPublicContract.DefaultCall))!;
        Assert.Equal(OperationAccess.Public, CallPolicy.AccessOf(typeof(IPublicContract), publicContractMethod));

        var internalContractMethod = typeof(IInternalContract).GetMethod(nameof(IInternalContract.DefaultCall))!;
        Assert.Equal(OperationAccess.Internal, CallPolicy.AccessOf(typeof(IInternalContract), internalContractMethod));
    }

    [Fact]
    public void Bind_binds_contract_and_throws_if_not_implemented()
    {
        var binder = new NeuronBinder();
        var call = Invocation(nameof(ITestContract.MutatingCall));

        var bound = CallPolicy.Bind(binder, typeof(DerivedNeuron), call);
        Assert.NotNull(bound);
        Assert.Equal(nameof(ITestContract.MutatingCall), bound.Method.Name);

        var unimplementedCall = new RuntimeInvocation("b", "NonExistentContract", "k", "M", []);
        var error = Assert.Throws<BindingException>(() => CallPolicy.Bind(binder, typeof(DerivedNeuron), unimplementedCall));
        Assert.Contains(nameof(DerivedNeuron), error.Message, StringComparison.Ordinal);
    }
}
