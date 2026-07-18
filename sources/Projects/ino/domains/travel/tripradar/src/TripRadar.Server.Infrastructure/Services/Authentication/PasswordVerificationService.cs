using TripRadar.Server.Application.Contracts.Services.Authentication;

namespace TripRadar.Server.Infrastructure.Services.Authentication;

public class PasswordVerificationService : IPasswordVerificationService
{
    // Computed once to keep timing consistent without hardcoding a hash.
    private static readonly string _dummyHash = BCrypt.Net.BCrypt.HashPassword("dummy", BCrypt.Net.BCrypt.GenerateSalt());
    // Limits concurrent dummy checks to reduce CPU spikes from invalid login attempts.
    private static readonly int _dummyCheckConcurrencyLimit = Math.Max(2, Environment.ProcessorCount / 2);
    private static readonly SemaphoreSlim _dummyCheckSemaphore = new(_dummyCheckConcurrencyLimit, _dummyCheckConcurrencyLimit);
    // Limits concurrent password verifications to reduce thread pool saturation under load.
    private static readonly int _verifyConcurrencyLimit = Math.Max(2, Environment.ProcessorCount);
    private static readonly SemaphoreSlim _verifySemaphore = new(_verifyConcurrencyLimit, _verifyConcurrencyLimit);

    public async Task<bool> VerifyAsync(string password, string hash, CancellationToken cancellationToken = default)
    {
        await _verifySemaphore.WaitAsync(cancellationToken);
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        finally
        {
            _verifySemaphore.Release();
        }
    }

    public async Task ConsumeDummyCheckAsync(string password, CancellationToken cancellationToken = default)
    {
        await _dummyCheckSemaphore.WaitAsync(cancellationToken);
        try
        {
            BCrypt.Net.BCrypt.Verify(password, _dummyHash);
        }
        finally
        {
            _dummyCheckSemaphore.Release();
        }
    }
}
