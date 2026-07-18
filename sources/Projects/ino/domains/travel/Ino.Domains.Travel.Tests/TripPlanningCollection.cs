using Ino.Testing.E2E;
using Xunit;

namespace Ino.Domains.Travel.Tests;

[CollectionDefinition(nameof(TripPlanningCollection))]
public sealed class TripPlanningCollection : InoBrowserCollection<Projects.Ino_AppHost> { }
