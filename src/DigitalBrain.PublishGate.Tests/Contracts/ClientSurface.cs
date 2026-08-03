using System.ComponentModel;
using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using Xunit;

namespace DigitalBrain.Tests.Contracts;

public sealed class ClientSurface
{
    [Fact(DisplayName =
        "IDigitalBrain surface is ambient Owner + Activate/Get/Emit + temporary grain proxy — no journal observation")]
    public void ProgrammingModelIsOwnerGetAndEmitOnly()
    {
        Assert.Contains(typeof(IDigitalBrain), typeof(DigitalBrainClient).GetInterfaces());

        Assert.Equal(
            2,
            typeof(IDigitalBrain).GetMethods()
                .Count(method => method.Name == nameof(IDigitalBrain.SendAsync)));
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

    [Fact(DisplayName = "Get returns a neuron reference constrained to neuron contracts")]
    public void GetReturnsNeuronReferenceConstrainedToNeurons()
    {
        var get = typeof(IDigitalBrain).GetMethods()
            .Single(method => method.Name == nameof(IDigitalBrain.Get) && method.GetParameters().Length == 1);

        Assert.True(get.ReturnType.IsGenericType);
        Assert.Equal("NeuronReference`1", get.ReturnType.GetGenericTypeDefinition().Name);

        var constraints = get.GetGenericArguments().Single().GetGenericParameterConstraints();
        Assert.Contains(typeof(INeuron), constraints);
    }

    [Fact(DisplayName = "GetGrainProxy is the temporary method-shaped seam and is hidden from authors")]
    public void GetGrainProxyIsTemporaryHiddenSeam()
    {
        var proxy = typeof(IDigitalBrain).GetMethod(nameof(IDigitalBrain.GetGrainProxy));
        Assert.NotNull(proxy);
        Assert.Equal(
            EditorBrowsableState.Never,
            proxy.GetCustomAttribute<EditorBrowsableAttribute>()?.State);
        Assert.True(proxy.ReturnType.IsGenericParameter);
        Assert.Contains(
            typeof(INeuron),
            proxy.GetGenericArguments().Single().GetGenericParameterConstraints());
        Assert.True(proxy.GetGenericArguments().Single().GenericParameterAttributes
            .HasFlag(GenericParameterAttributes.ReferenceTypeConstraint));
    }

    [Fact(DisplayName = "NeuronReference exposes one-way and typed request SendAsync")]
    public void NeuronReferenceExposesDirectedSend()
    {
        var referenceType = typeof(DigitalBrainClient).Assembly
            .GetExportedTypes()
            .Single(type => type.Name == "NeuronReference`1");

        var sends = referenceType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == "SendAsync" && !method.IsSpecialName)
            .ToArray();

        Assert.Equal(2, sends.Length);

        var oneWay = sends.Single(method => !method.IsGenericMethodDefinition);
        Assert.Equal(typeof(Task), oneWay.ReturnType);
        Assert.Equal(
            [typeof(Synapse), typeof(CancellationToken)],
            oneWay.GetParameters().Select(parameter => parameter.ParameterType));

        var typed = sends.Single(method => method.IsGenericMethodDefinition);
        Assert.True(typed.ReturnType.IsGenericType);
        Assert.Equal(typeof(Task<>), typed.ReturnType.GetGenericTypeDefinition());
        Assert.Equal(
            typeof(RequestSynapse<>).Name,
            typed.GetParameters()[0].ParameterType.GetGenericTypeDefinition().Name);
    }

    [Fact(DisplayName = "RequestSynapse is a typed request contract over Synapse")]
    public void RequestSynapseIsTypedRequestContract()
    {
        Assert.True(typeof(RequestSynapse<>).IsAbstract);
        Assert.Equal(typeof(Synapse), typeof(RequestSynapse<>).BaseType);
        Assert.Equal(
            typeof(Synapse),
            typeof(RequestSynapse<>).GetGenericArguments().Single().GetGenericParameterConstraints().Single());
    }

    [Fact(DisplayName = "ProtectedPayloadReference is an opaque identity with optional expiry and no payload API")]
    public void ProtectedPayloadReferenceIsOpaque()
    {
        var members = typeof(ProtectedPayloadReference)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(member => member is not MethodInfo { IsSpecialName: true })
            .Select(member => member.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Contains(nameof(ProtectedPayloadReference.Id), members);
        Assert.Contains(nameof(ProtectedPayloadReference.ExpiresAt), members);
        Assert.DoesNotContain(members, name =>
            name.Contains("Payload", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Provider", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Storage", StringComparison.OrdinalIgnoreCase));

        var reference = new ProtectedPayloadReference(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1));
        Assert.NotEqual(Guid.Empty, reference.Id);
        Assert.NotNull(reference.ExpiresAt);
        Assert.Throws<ArgumentException>(() => new ProtectedPayloadReference(Guid.Empty));
    }

    [Fact(DisplayName = "client-level Send accepts Synapse facts; typed Get.Send is neuron-bound")]
    public void SendAndEmitCarrySynapseVocabulary()
    {
        var directSend = typeof(IDigitalBrain).GetMethods()
            .Single(method =>
                method.Name == nameof(IDigitalBrain.SendAsync)
                && !method.IsGenericMethodDefinition);
        var emit = typeof(IDigitalBrain).GetMethod(nameof(IDigitalBrain.EmitAsync));

        Assert.NotNull(emit);
        Assert.Equal(
            [typeof(NeuronId), typeof(Synapse), typeof(CancellationToken)],
            directSend.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            [typeof(Synapse), typeof(CancellationToken)],
            emit.GetParameters().Select(parameter => parameter.ParameterType));
    }
}
