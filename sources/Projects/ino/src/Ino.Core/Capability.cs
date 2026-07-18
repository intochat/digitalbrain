using System.Collections.Immutable;

namespace Ino.Core;

/// <summary>
/// Discriminated union of capabilities a neuron may require. Aggregated at
/// compile time by the source generator (Phase 3) into DomainMetadata.RequiredCapabilities
/// and surfaced at install time via the marketplace consent screen.
/// </summary>
public abstract record Capability
{
    public sealed record Http : Capability
    {
        public Http(params string[]? allowedHosts)
        {
            AllowedHosts = allowedHosts is null
                ? ImmutableArray<string>.Empty
                : [..allowedHosts];
        }

        public ImmutableArray<string> AllowedHosts { get; }

        public bool Equals(Http? other) =>
            other is not null && base.Equals(other) && AllowedHosts.SequenceEqual(other.AllowedHosts);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(typeof(Http));
            foreach (var host in AllowedHosts) hash.Add(host);
            return hash.ToHashCode();
        }
    }

    public sealed record Llm(LlmTier Tier = LlmTier.Balanced) : Capability;

    public sealed record Persistence(string StoragePrefix) : Capability;

    public sealed record Identity : Capability
    {
        public Identity(string provider, params string[]? scopes)
        {
            Provider = provider;
            Scopes = scopes is null ? ImmutableArray<string>.Empty : [..scopes];
        }

        public string Provider { get; }
        public ImmutableArray<string> Scopes { get; }

        public bool Equals(Identity? other) =>
            other is not null && base.Equals(other)
            && Provider == other.Provider && Scopes.SequenceEqual(other.Scopes);

        public override int GetHashCode() =>
            HashCode.Combine(typeof(Identity), Provider, StructuralHash(Scopes));

        private static int StructuralHash(ImmutableArray<string> items)
        {
            var hash = new HashCode();
            foreach (var item in items) hash.Add(item);
            return hash.ToHashCode();
        }
    }

    public sealed record LocalFile(string PathPattern) : Capability;
}
