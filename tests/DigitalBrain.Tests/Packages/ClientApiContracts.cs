using System.ComponentModel;
using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using Xunit;

namespace DigitalBrain.Tests.Packages;

public sealed class ClientApiContracts
{
    [Fact(DisplayName = "Client package exports only IDigitalBrain and DigitalBrainClient")]
    public void PublicExportsAreProgrammingModelOnly()
    {
        var exports = typeof(DigitalBrainClient).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(DigitalBrainClient),
                nameof(IDigitalBrain),
            ],
            exports);
    }

    [Fact(DisplayName =
        "IDigitalBrain surface is ambient Owner + Activate/Get/Send/Emit only — no journal observation")]
    public void ProgrammingModelIsOwnerGetSendAndEmitOnly()
    {
        Assert.Contains(typeof(IDigitalBrain), typeof(DigitalBrainClient).GetInterfaces());

        var methods = typeof(IDigitalBrain)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(IDigitalBrain.ActivateAsync),
                nameof(IDigitalBrain.EmitAsync),
                nameof(IDigitalBrain.Get),
                nameof(IDigitalBrain.SendAsync),
            ],
            methods);

        var properties = typeof(IDigitalBrain)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([nameof(IDigitalBrain.Owner)], properties);
    }

    [Fact(DisplayName = "owner is ambient on every IDigitalBrain operation")]
    public void OwnerIsAmbientOnEveryOperation()
    {
        var methods = typeof(IDigitalBrain)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToArray();

        Assert.NotEmpty(methods);

        Assert.All(methods, method =>
        {
            Assert.DoesNotContain(
                method.GetParameters(),
                parameter => parameter.ParameterType == typeof(OwnerId)
                    || (parameter.Name is not null
                        && parameter.Name.Contains(
                            nameof(IDigitalBrain.Owner),
                            StringComparison.OrdinalIgnoreCase)));
        });
    }

    [Fact(DisplayName = "DigitalBrainClient author surface is Activate/Get/Send/Emit; Connect is wiring only")]
    public void ClientSurfaceIsGetSendEmitWithWiringConnect()
    {
        var methods = typeof(DigitalBrainClient)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.DeclaringType == typeof(DigitalBrainClient) && !method.IsSpecialName)
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(DigitalBrainClient.ActivateAsync),
                nameof(DigitalBrainClient.Connect),
                nameof(DigitalBrainClient.EmitAsync),
                nameof(DigitalBrainClient.Get),
                nameof(DigitalBrainClient.SendAsync),
            ],
            methods);
        Assert.NotNull(typeof(DigitalBrainClient).GetProperty(nameof(DigitalBrainClient.Owner)));

        var connect = typeof(DigitalBrainClient).GetMethod(
            nameof(DigitalBrainClient.Connect),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(connect);
        Assert.Equal(
            EditorBrowsableState.Never,
            connect.GetCustomAttribute<EditorBrowsableAttribute>()?.State);
        Assert.DoesNotContain(
            typeof(IDigitalBrain).GetMethods(BindingFlags.Public | BindingFlags.Instance),
            method => method.Name == nameof(DigitalBrainClient.Connect));
    }

    [Fact(DisplayName = "Get is constrained to neuron contracts")]
    public void GetIsNeuronConstrained()
    {
        var get = typeof(IDigitalBrain).GetMethods()
            .Single(method => method.Name == nameof(IDigitalBrain.Get) && method.GetParameters().Length == 1);

        var constraints = get.GetGenericArguments().Single().GetGenericParameterConstraints();

        Assert.Contains(typeof(INeuron), constraints);
        Assert.True(get.GetGenericArguments().Single().GenericParameterAttributes
            .HasFlag(GenericParameterAttributes.ReferenceTypeConstraint));
    }

    [Fact(DisplayName = "Send and Emit accept Synapse facts; typed Send is neuron-bound")]
    public void SendAndEmitCarrySynapseVocabulary()
    {
        var typedSend = typeof(IDigitalBrain).GetMethods()
            .Single(method =>
                method.Name == nameof(IDigitalBrain.SendAsync)
                && method.IsGenericMethodDefinition);
        var directSend = typeof(IDigitalBrain).GetMethods()
            .Single(method =>
                method.Name == nameof(IDigitalBrain.SendAsync)
                && !method.IsGenericMethodDefinition);
        var emit = typeof(IDigitalBrain).GetMethod(nameof(IDigitalBrain.EmitAsync));

        Assert.NotNull(emit);
        Assert.Equal(
            [typeof(string), typeof(Synapse)],
            typedSend.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Contains(typeof(INeuron), typedSend.GetGenericArguments().Single().GetGenericParameterConstraints());
        Assert.Equal(
            [typeof(NeuronId), typeof(Synapse)],
            directSend.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            [typeof(Synapse)],
            emit.GetParameters().Select(parameter => parameter.ParameterType));
    }
}
