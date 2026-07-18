# Ino.Domains.Taxi.Tests

E2E tests for the Taxi domain neurons. Currently a scaffold — Taxi is a
v0.1 placeholder ("Uber via MCP, scaffold-only" per the product vision).

When the first Taxi neuron lands (e.g., RequestRide), add a test class:

```csharp
[Collection(nameof(TaxiBrowserCollection))]
[Trait("Neuron", "RequestRide")]
public class RequestRideNeuronTests(InoBrowserFixture<Projects.Ino_AppHost> fx)
{
    // ...
}
```

Plus a `TaxiBrowserCollection : InoBrowserCollection<Projects.Ino_AppHost>`
collection-definition wrapper.

Drive the UI via the GoRouter `?q=...` deep-link auto-send and assert via
gRPC-Web response interception — same pattern as
`domains/travel/Ino.Domains.Travel.Tests/TripPlanningNeuronTests`.
