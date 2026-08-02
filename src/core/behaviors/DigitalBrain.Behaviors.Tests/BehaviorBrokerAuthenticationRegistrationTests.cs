using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using DigitalBrain.Behaviors.Runtime;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorBrokerAuthenticationRegistrationTests
{
    [Fact(DisplayName = "registration refuses a missing broker credential outside Development")]
    public void MissingCredentialOutsideDevelopmentThrowsActionableError()
    {
        var configuration = new ConfigurationBuilder().Build();
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(BehaviorBrokerAuthenticationRegistrationTests).Assembly.FullName,
            EnvironmentName = Environments.Production,
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.Services.AddBehaviorBrokerAuthentication(configuration, builder.Environment));

        Assert.Contains(BehaviorBrokerContract.CredentialConfigurationKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "registration keeps tolerating a missing broker credential in Development")]
    public void MissingCredentialInDevelopmentStaysTolerant()
    {
        var configuration = new ConfigurationBuilder().Build();
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(BehaviorBrokerAuthenticationRegistrationTests).Assembly.FullName,
            EnvironmentName = Environments.Development,
        });

        var exception = Record.Exception(
            () => builder.Services.AddBehaviorBrokerAuthentication(configuration, builder.Environment));

        Assert.Null(exception);
    }
}
