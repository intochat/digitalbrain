namespace DigitalBrain.Runtime.Brain;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;

[GenerateSerializer]
public sealed class BrainRegistryState
{
    [Id(0)] public List<BrainMetadata> Brains { get; set; } = new();
}

[GrainType("BrainRegistryGrain")]
public sealed class BrainRegistryGrain(
    [PersistentState("brains-list", "digitalbrain")] IPersistentState<BrainRegistryState> state)
    : Grain, IBrainRegistry
{
    private static readonly Regex SlugRegex = new(@"^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.Compiled);

    public Task<BrainId> CreateBrainAsync(string name, string? seedTemplate = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Brain name cannot be empty.", nameof(name));

        var slug = name.Trim().ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-");

        // Clean up any double hyphens or invalid characters
        slug = Regex.Replace(slug, @"[^a-z0-9-]", "");
        slug = Regex.Replace(slug, @"-+", "-");
        slug = slug.Trim('-');

        if (string.IsNullOrWhiteSpace(slug) || !SlugRegex.IsMatch(slug))
            throw new ArgumentException($"Brain name '{name}' produces invalid slug '{slug}'.", nameof(name));

        if (state.State.Brains.Any(b => b.BrainId.Equals(slug, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Brain with ID '{slug}' already exists.");

        var metadata = new BrainMetadata(slug, name, DateTimeOffset.UtcNow);
        state.State.Brains.Add(metadata);
        
        return state.WriteStateAsync().ContinueWith(_ => new BrainId(slug));
    }

    public Task DeleteBrainAsync(string brainId)
    {
        if (string.IsNullOrWhiteSpace(brainId))
            throw new ArgumentException("Brain ID cannot be empty.", nameof(brainId));

        var existing = state.State.Brains.FirstOrDefault(b => b.BrainId.Equals(brainId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            state.State.Brains.Remove(existing);
            return state.WriteStateAsync();
        }

        return Task.CompletedTask;
    }

    public Task RenameBrainAsync(string brainId, string newName)
    {
        if (string.IsNullOrWhiteSpace(brainId))
            throw new ArgumentException("Brain ID cannot be empty.", nameof(brainId));
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("New name cannot be empty.", nameof(newName));

        var existingIndex = state.State.Brains.FindIndex(b => b.BrainId.Equals(brainId, StringComparison.OrdinalIgnoreCase));
        if (existingIndex == -1)
            throw new KeyNotFoundException($"Brain with ID '{brainId}' not found.");

        var oldMetadata = state.State.Brains[existingIndex];
        state.State.Brains[existingIndex] = oldMetadata with { Name = newName };
        return state.WriteStateAsync();
    }

    public Task<IReadOnlyList<BrainMetadata>> ListBrainsAsync()
    {
        return Task.FromResult<IReadOnlyList<BrainMetadata>>(state.State.Brains.AsReadOnly());
    }

    public Task<BrainMetadata?> GetBrainAsync(string brainId)
    {
        if (string.IsNullOrWhiteSpace(brainId))
            return Task.FromResult<BrainMetadata?>(null);

        var existing = state.State.Brains.FirstOrDefault(b => b.BrainId.Equals(brainId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(existing);
    }
}
