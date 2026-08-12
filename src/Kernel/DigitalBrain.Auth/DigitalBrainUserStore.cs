using Microsoft.AspNetCore.Identity;

namespace DigitalBrain.Auth;

public sealed class DigitalBrainUserStore(IAccountDirectory accounts) :
    IUserStore<DigitalBrainUser>,
    IUserPasswordStore<DigitalBrainUser>,
    IUserSecurityStampStore<DigitalBrainUser>
{
    public Task<string> GetUserIdAsync(DigitalBrainUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(user.Id);
    }

    public Task<string?> GetUserNameAsync(DigitalBrainUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(user.UserName);
    }

    public Task SetUserNameAsync(DigitalBrainUser user, string? userName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        user.UserName = userName ?? "";
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(DigitalBrainUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(user.NormalizedUserName);
    }

    public Task SetNormalizedUserNameAsync(
        DigitalBrainUser user,
        string? normalizedName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        user.NormalizedUserName = normalizedName ?? "";
        return Task.CompletedTask;
    }

    public async Task<IdentityResult> CreateAsync(DigitalBrainUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();

        if (user.PrincipalId == Guid.Empty)
        {
            user.PrincipalId = Guid.NewGuid();
        }

        if (user.CreatedAt == default)
        {
            user.CreatedAt = DateTimeOffset.UtcNow;
        }

        try
        {
            await accounts.CreateAsync(user, cancellationToken).ConfigureAwait(false);
            return IdentityResult.Success;
        }
        catch (InvalidOperationException ex)
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "DuplicateUser",
                Description = ex.Message,
            });
        }
    }

    public async Task<IdentityResult> UpdateAsync(DigitalBrainUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await accounts.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
            return IdentityResult.Success;
        }
        catch (InvalidOperationException ex)
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "UpdateFailed",
                Description = ex.Message,
            });
        }
    }

    public Task<IdentityResult> DeleteAsync(DigitalBrainUser user, CancellationToken cancellationToken)
        => throw new NotSupportedException("DigitalBrain does not delete installation accounts.");

    public Task<DigitalBrainUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
        => accounts.FindByIdAsync(userId, cancellationToken);

    public Task<DigitalBrainUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        => accounts.FindByNameAsync(normalizedUserName, cancellationToken);

    public Task SetPasswordHashAsync(DigitalBrainUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(DigitalBrainUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(user.PasswordHash);
    }

    public Task<bool> HasPasswordAsync(DigitalBrainUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));
    }

    public Task SetSecurityStampAsync(DigitalBrainUser user, string stamp, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(stamp);
        cancellationToken.ThrowIfCancellationRequested();
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(DigitalBrainUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(user.SecurityStamp);
    }

    public void Dispose()
    {
    }
}
