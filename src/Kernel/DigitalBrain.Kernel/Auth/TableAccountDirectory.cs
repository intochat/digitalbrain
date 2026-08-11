using Azure;
using Azure.Data.Tables;

namespace DigitalBrain.Kernel;

internal sealed class TableAccountDirectory(TableClient users) : IAccountDirectory
{
    private const string NamePartition = "name";
    private const string IdPartition = "id";
    private const string BootstrapPartition = "meta";
    private const string BootstrapRow = "bootstrap-owner";

    private int _tableReady;

    public async Task<bool> IsEmptyAsync(CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var _ in users.QueryAsync<TableEntity>(
            filter: $"PartitionKey eq '{NamePartition}'",
            maxPerPage: 1,
            cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        return true;
    }

    public async Task<DigitalBrainUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        await EnsureTableAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var entity = await users
                .GetEntityAsync<TableEntity>(IdPartition, userId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return FromEntity(entity.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }

    public async Task<DigitalBrainUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUserName);
        await EnsureTableAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var entity = await users
                .GetEntityAsync<TableEntity>(NamePartition, normalizedUserName, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return FromEntity(entity.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }

    public async Task<DigitalBrainUser?> FindBootstrapOwnerAsync(CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var marker = await users
                .GetEntityAsync<TableEntity>(BootstrapPartition, BootstrapRow, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var userId = marker.Value.GetString("UserId");
            return userId is null ? null : await FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }

    public async Task CreateAsync(DigitalBrainUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        await EnsureTableAsync(cancellationToken).ConfigureAwait(false);

        var nameEntity = ToEntity(NamePartition, user.NormalizedUserName, user);
        var idEntity = ToEntity(IdPartition, user.Id, user);

        try
        {
            await users.AddEntityAsync(nameEntity, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status409Conflict)
        {
            throw new InvalidOperationException($"Username '{user.UserName}' is already taken.", ex);
        }

        try
        {
            await users.AddEntityAsync(idEntity, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status409Conflict)
        {
            await users.DeleteEntityAsync(NamePartition, user.NormalizedUserName, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidOperationException($"Account '{user.Id}' already exists.", ex);
        }

        if (user.IsBootstrapOwner)
        {
            await users.UpsertEntityAsync(
                new TableEntity(BootstrapPartition, BootstrapRow)
                {
                    ["UserId"] = user.Id,
                },
                TableUpdateMode.Replace,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task UpdateAsync(DigitalBrainUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        await EnsureTableAsync(cancellationToken).ConfigureAwait(false);

        await users.UpsertEntityAsync(
            ToEntity(NamePartition, user.NormalizedUserName, user),
            TableUpdateMode.Replace,
            cancellationToken).ConfigureAwait(false);
        await users.UpsertEntityAsync(
            ToEntity(IdPartition, user.Id, user),
            TableUpdateMode.Replace,
            cancellationToken).ConfigureAwait(false);

        if (user.IsBootstrapOwner)
        {
            await users.UpsertEntityAsync(
                new TableEntity(BootstrapPartition, BootstrapRow)
                {
                    ["UserId"] = user.Id,
                },
                TableUpdateMode.Replace,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _tableReady, 1, 0) != 0)
        {
            return;
        }

        await users.CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TableEntity ToEntity(string partition, string row, DigitalBrainUser user)
        => new(partition, row)
        {
            ["UserId"] = user.Id,
            ["UserName"] = user.UserName,
            ["NormalizedUserName"] = user.NormalizedUserName,
            ["PasswordHash"] = user.PasswordHash,
            ["SecurityStamp"] = user.SecurityStamp,
            ["PrincipalId"] = user.PrincipalId.ToString("N"),
            ["IsBootstrapOwner"] = user.IsBootstrapOwner,
            ["CreatedAt"] = user.CreatedAt,
        };

    private static DigitalBrainUser FromEntity(TableEntity entity)
    {
        var principalText = entity.GetString("PrincipalId")
            ?? throw new InvalidOperationException("Identity row is missing PrincipalId.");
        if (!Guid.TryParse(principalText, out var principalId) || principalId == Guid.Empty)
        {
            throw new InvalidOperationException("Identity row has an invalid PrincipalId.");
        }

        return new DigitalBrainUser
        {
            Id = entity.GetString("UserId") ?? entity.RowKey,
            UserName = entity.GetString("UserName") ?? "",
            NormalizedUserName = entity.GetString("NormalizedUserName") ?? entity.RowKey,
            PasswordHash = entity.GetString("PasswordHash"),
            SecurityStamp = entity.GetString("SecurityStamp") ?? Guid.NewGuid().ToString("N"),
            PrincipalId = principalId,
            IsBootstrapOwner = entity.GetBoolean("IsBootstrapOwner") ?? false,
            CreatedAt = entity.GetDateTimeOffset("CreatedAt") ?? DateTimeOffset.UnixEpoch,
        };
    }
}
