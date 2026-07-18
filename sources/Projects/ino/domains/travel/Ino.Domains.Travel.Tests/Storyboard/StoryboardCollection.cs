using Xunit;

namespace Ino.Domains.Travel.Tests.Storyboard;

[CollectionDefinition(nameof(StoryboardCollection))]
public sealed class StoryboardCollection : ICollectionFixture<StoryboardTestSiloFixture> { }
