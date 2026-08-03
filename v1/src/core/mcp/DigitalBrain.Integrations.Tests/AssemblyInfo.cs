using DigitalBrain.Integrations.Tests;
using Xunit;

[assembly: AssemblyFixture(typeof(IntegrationsFixture))]
[assembly: AssemblyFixture(typeof(AuthorizationRailFixture))]
[assembly: AssemblyFixture(typeof(AuthorizationProviderProofFixture))]
[assembly: AssemblyFixture(typeof(McpProviderHoldOpenProofFixture))]
[assembly: AssemblyFixture(typeof(UserActionProductionRailFixture))]
