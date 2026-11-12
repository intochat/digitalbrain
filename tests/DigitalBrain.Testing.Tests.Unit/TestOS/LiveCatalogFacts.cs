using DigitalBrain.Units;
using System.Reflection;
using System.Reflection.Emit;
using DigitalBrain.Kernel;
using DigitalBrain.Testing.Tests.Unit.Synapses;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Live;

// What a reaction added after activation does; called from a type emitted at run time, so the assembly scan never sees it.
public static class LateBooks
{
    public static Ledger AddTenfold(Charging s, Ledger w)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(w);
        return w with { Spent = w.Spent + (s.Amount * 10m) };
    }

    // A static class with one [Synapse(typeof(Charging), "ledger")] reaction forwarding to AddTenfold.
    internal static Type Emit()
    {
        var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Late" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run).DefineDynamicModule("m");
        var type = module.DefineType("LateReaction", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var method = type.DefineMethod("Tenfold", MethodAttributes.Public | MethodAttributes.Static, typeof(Ledger), [typeof(Charging), typeof(Ledger)]);
        method.SetCustomAttribute(new CustomAttributeBuilder(typeof(SynapseAttribute).GetConstructor([typeof(Type), typeof(string)])!, [typeof(Charging), "ledger"]));
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(LateBooks).GetMethod(nameof(AddTenfold))!);
        il.Emit(OpCodes.Ret);
        return type.CreateType();
    }
}

public sealed class LiveCatalogFacts
{
    private static readonly NeuronId Books = NeuronId.Parse("lc/ledger/default");
    private static readonly NeuronId Pay = NeuronId.Parse("lc/payments/p");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<OS> Start(string alias)
    {
        var os = OS.Local().Register(typeof(StateNeuronOnTestOSFacts).Assembly);
        os.Confirmations.ApproveAll();
        await os.Get<IInstalls>(NeuronId.Parse("lc/synapse.installs/default")).Install(new InstallRequest(alias, new Dictionary<string, string>()), Ct);
        await os.Idle();
        return os;
    }

    [Fact]
    public async Task ANeuronActivatedBeforeARegisterSeesTheNewSynapses()
    {
        await using var os = await Start("ledger");
        await os.Activate(Books);
        await os.Idle();

        os.Source.Replace("ledger", SynapseCatalog.Scan([typeof(Ledger), typeof(Synapses.Books), LateBooks.Emit()]));
        await os.Get<IPayments>(Pay).Charge(5m);
        await os.Idle();

        Assert.True(os.IsActive(Books));
        var state = await ((IBrainObserver)os).State(Books, Ct);
        Assert.Equal(55m, state?.GetProperty("spent").GetDecimal());
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task UnregisterRemovesTheAliasFromCatalog()
    {
        await using var os = await Start("week");

        os.Unregister("week");

        Assert.False(os.Catalog.States.ContainsKey("week"));
        Assert.Throws<InvalidOperationException>(() => os.Get<IInstalls>(new NeuronId(Pay.Brain, "week", "default")));
        await os.Get<IPayments>(Pay).Charge(400m);
        await os.Idle();
        Assert.Empty(os.Failures);
    }
}
