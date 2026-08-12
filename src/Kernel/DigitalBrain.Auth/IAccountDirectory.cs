namespace DigitalBrain.Auth;

public interface IAccountDirectory
{
    Task<bool> IsEmptyAsync(CancellationToken cancellationToken);

    Task<DigitalBrainUser?> FindByIdAsync(string userId, CancellationToken cancellationToken);

    Task<DigitalBrainUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken);

    Task<DigitalBrainUser?> FindBootstrapOwnerAsync(CancellationToken cancellationToken);

    Task CreateAsync(DigitalBrainUser user, CancellationToken cancellationToken);

    Task UpdateAsync(DigitalBrainUser user, CancellationToken cancellationToken);
}
