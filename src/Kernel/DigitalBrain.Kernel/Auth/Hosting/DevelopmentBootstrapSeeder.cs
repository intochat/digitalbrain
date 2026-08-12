using DigitalBrain.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace DigitalBrain.Kernel;

// Cold Azurite / empty identity tables leave loopback auth unable to mint a principal,
// so Flutter gets 401 on every product endpoint. Development + loopback: seed once.
internal sealed class DevelopmentBootstrapSeeder(
    IServiceScopeFactory scopes,
    IHostEnvironment environment,
    LoopbackDevAuthOptions loopback,
    IConfiguration configuration,
    ILogger<DevelopmentBootstrapSeeder> log) : IHostedService
{
    public const string DefaultUsername = "owner";
    public const string DefaultPassword = "ownerowner";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() || !loopback.Enabled)
        {
            return;
        }

        // Orleans + tables may still be coming up; retry briefly so Flutter's first
        // connection after AppHost healthy has a bootstrap owner for loopback auth.
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                if (await TrySeedAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                return; // table reachable, already has accounts
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < 30)
            {
                log.LogDebug(
                    ex,
                    "Development bootstrap seed attempt {Attempt} deferred (storage not ready).",
                    attempt);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<bool> TrySeedAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountDirectory>();
        if (!await accounts.IsEmptyAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (await accounts.FindBootstrapOwnerAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            return false;
        }

        var username = configuration["DigitalBrain:Auth:DevBootstrapUsername"];
        if (string.IsNullOrWhiteSpace(username))
        {
            username = DefaultUsername;
        }

        var password = configuration["DigitalBrain:Auth:DevBootstrapPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            password = DefaultPassword;
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<DigitalBrainUser>>();
        var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceMembershipGateway>();
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var principalId = PrincipalId.New();
        var user = new DigitalBrainUser
        {
            UserName = username.Trim(),
            PrincipalId = principalId.Value,
            IsBootstrapOwner = true,
            CreatedAt = time.GetUtcNow(),
        };

        var created = await users.CreateAsync(user, password).ConfigureAwait(false);
        if (!created.Succeeded)
        {
            // Race with another seed/bootstrap — treat as done if owner now exists.
            if (await accounts.FindBootstrapOwnerAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                return false;
            }

            throw new InvalidOperationException(
                "Development bootstrap seed failed: "
                + string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        user = await users.FindByIdAsync(user.Id).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Development bootstrap account vanished after create.");

        var actor = new ActorContext(principalId, user.UserName);
        await workspace.AddMemberAsync(
            actor,
            principalId,
            user.UserName,
            WorkspaceRole.Owner,
            cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Development bootstrap owner '{Username}' seeded for loopback Flutter auth (principal {Principal}).",
            user.UserName,
            principalId.Value.ToString("N"));
        return true;
    }
}
