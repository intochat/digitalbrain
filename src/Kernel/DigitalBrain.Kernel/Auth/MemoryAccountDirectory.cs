using System.Collections.Concurrent;

namespace DigitalBrain.Kernel;

internal sealed class MemoryAccountDirectory : IAccountDirectory
{
    private readonly ConcurrentDictionary<string, DigitalBrainUser> _byId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _idByNormalizedName = new(StringComparer.Ordinal);

    public Task<bool> IsEmptyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_byId.IsEmpty);
    }

    public Task<DigitalBrainUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_byId.TryGetValue(userId, out var user) ? Clone(user) : null);
    }

    public Task<DigitalBrainUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUserName);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_idByNormalizedName.TryGetValue(normalizedUserName, out var id))
        {
            return Task.FromResult<DigitalBrainUser?>(null);
        }

        return FindByIdAsync(id, cancellationToken);
    }

    public Task<DigitalBrainUser?> FindBootstrapOwnerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = _byId.Values.FirstOrDefault(user => user.IsBootstrapOwner);
        return Task.FromResult(owner is null ? null : Clone(owner));
    }

    public Task CreateAsync(DigitalBrainUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(user.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(user.NormalizedUserName);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_byId.TryAdd(user.Id, Clone(user)))
        {
            throw new InvalidOperationException($"Account '{user.Id}' already exists.");
        }

        if (!_idByNormalizedName.TryAdd(user.NormalizedUserName, user.Id))
        {
            _byId.TryRemove(user.Id, out _);
            throw new InvalidOperationException($"Username '{user.UserName}' is already taken.");
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(DigitalBrainUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(user.Id);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_byId.ContainsKey(user.Id))
        {
            throw new InvalidOperationException($"Account '{user.Id}' does not exist.");
        }

        _byId[user.Id] = Clone(user);
        _idByNormalizedName[user.NormalizedUserName] = user.Id;
        return Task.CompletedTask;
    }

    private static DigitalBrainUser Clone(DigitalBrainUser user)
        => new()
        {
            Id = user.Id,
            UserName = user.UserName,
            NormalizedUserName = user.NormalizedUserName,
            PasswordHash = user.PasswordHash,
            SecurityStamp = user.SecurityStamp,
            PrincipalId = user.PrincipalId,
            IsBootstrapOwner = user.IsBootstrapOwner,
            CreatedAt = user.CreatedAt,
        };
}
