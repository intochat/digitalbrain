using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Salesforce;
using DigitalBrain.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CompiledModuleContracts
{
    private static readonly Type[] Modules =
    [
        typeof(AIModule),
        typeof(TasksModule),
        typeof(GoogleModule),
        typeof(SalesforceModule),
    ];

    [Fact]
    public void EveryModuleHasOneGeneratedFullyQualifiedIdentity()
    {
        var identities = Modules
            .Select(type => (type, Id: ((ICompiledModule)Activator.CreateInstance(type)!).Id))
            .ToArray();

        Assert.All(identities, entry => Assert.Equal(entry.type.FullName, entry.Id.Value));
        Assert.Equal(identities.Length, identities.Select(entry => entry.Id).Distinct().Count());
    }

    [Fact]
    public void CapsuleAbiIsHiddenFromNormalIntellisense()
    {
        var attribute = typeof(ICompiledModule)
            .GetCustomAttributes(typeof(EditorBrowsableAttribute), inherit: false)
            .Cast<EditorBrowsableAttribute>()
            .Single();

        Assert.Equal(EditorBrowsableState.Never, attribute.State);
    }

    [Fact]
    public void SerializationPreparationIsReusableBySilosAndClients()
    {
        var method = typeof(ICompiledModule).GetMethod(nameof(ICompiledModule.PrepareSerialization));
        Assert.NotNull(method);
        var parameter = Assert.Single(method.GetParameters());

        Assert.Equal(typeof(IServiceCollection), parameter.ParameterType);
    }
}
