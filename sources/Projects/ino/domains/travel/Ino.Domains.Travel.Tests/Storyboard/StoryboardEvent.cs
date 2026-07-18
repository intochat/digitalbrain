using System.Text.Json;

namespace Ino.Domains.Travel.Tests.Storyboard;

public abstract record StoryboardEvent(double T, string Kind);

public sealed record OrbEvent(double T, string State)
    : StoryboardEvent(T, "orb");

public sealed record UtterEvent(double T, string Text)
    : StoryboardEvent(T, "utter");

public sealed record SynapseEvent(
    double T, string From, string To, JsonElement Payload, bool Gold)
    : StoryboardEvent(T, "syn");

public sealed record CardEvent(
    double T, string Id, string Stage, string? FromCluster)
    : StoryboardEvent(T, "card");
