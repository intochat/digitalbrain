using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

[CollectionDefinition(HostedApplication.CollectionName, DisableParallelization = true)]
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "xUnit requires collection definition types to be public.")]
public sealed class HostedCollectionDefinition : ICollectionFixture<HostedApplicationFixture>;
