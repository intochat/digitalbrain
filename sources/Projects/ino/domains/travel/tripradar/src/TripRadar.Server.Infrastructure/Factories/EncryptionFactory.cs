using Microsoft.Extensions.Options;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Comms.Core.Contracts.Encryptions;
using TripRadar.Server.Comms.Core.Encryptions;
using TripRadar.Server.Comms.Core.Exceptions;
using TripRadar.Server.Infrastructure.Constants;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.Infrastructure.Factories;

public class EncryptionFactory : IEncryptionFactory
{
    private readonly Dictionary<string, ICryptographer> _cryptographers = new();

    public EncryptionFactory(IOptions<EncryptionSettings> encryptionOptions)
    {
        ArgumentNullException.ThrowIfNull(encryptionOptions);
        InitializeCryptographers(encryptionOptions.Value);
    }

    public IEncryptor GetEncryptor(string cryptographerName) =>
        _cryptographers.TryGetValue(cryptographerName, out var cryptographer) ? cryptographer : throw new ObjectNotFoundException($"{cryptographerName} - {Errors.CryptographerNotFound.Reason}");

    public IDecryptor GetDecryptor(string cryptographerName) =>
        _cryptographers.TryGetValue(cryptographerName, out var cryptographer) ? cryptographer : throw new ObjectNotFoundException($"{cryptographerName} - {Errors.CryptographerNotFound.Reason}");

    private void InitializeCryptographers(EncryptionSettings settings)
    {
        var userDataKey = settings.UserDataKey;
        if (string.IsNullOrEmpty(userDataKey))
            throw new InvalidOperationException(
                "Encryption:UserDataKey is required for user data encryption.");

        _cryptographers[EncryptionConstants.UserDataCryptographer] = new AesCryptographer(EncryptionConstants.UserDataCryptographer, userDataKey);
    }
}
