using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TripRadar.Server.Comms.Core.Extensions;

namespace TripRadar.Server.Db.Seeding;

public static partial class DbSeeder
{
    private static string? GetEncryptedStripeId(IConfiguration configuration, string configKey, bool allowNull = false)
    {
        var stripeId = configuration[configKey];

        return string.IsNullOrWhiteSpace(stripeId)
            ? allowNull
                ? null
                : throw new InvalidOperationException(
                    $"Stripe price ID configuration '{configKey}' is missing or empty.")
            : stripeId.EncryptString();
    }

    private static string? GetStripeIdHash(IConfiguration configuration, string configKey, bool allowNull = false)
    {
        var stripeId = configuration[configKey];

        if (string.IsNullOrWhiteSpace(stripeId))
        {
            if (allowNull)
            {
                return null;
            }

            throw new InvalidOperationException($"Stripe price ID configuration '{configKey}' is missing or empty.");
        }

        var userDataKey = configuration["Encryption:UserDataKey"];
        if (string.IsNullOrWhiteSpace(userDataKey))
        {
            throw new InvalidOperationException("Encryption:UserDataKey is required for blind index hashing.");
        }

        var normalized = stripeId.Trim().ToLowerInvariant();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(userDataKey));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalized));

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }



    private static int GetCurrencyIdByCode(SetupDbContext ctx, string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new InvalidOperationException("Currency code is required.");

        var normalized = currencyCode.Trim().ToLowerInvariant();
        var currencyId = ctx.Currencies
            .AsNoTracking()
            .Where(c => c.CurrencyCode == normalized)
            .Select(c => c.CurrencyId)
            .FirstOrDefault();

        return currencyId == 0
            ? throw new InvalidOperationException($"Currency code '{currencyCode}' was not found in seeded data.")
            : currencyId;
    }

    private static async Task SeedDataFromJsonAsync<TModel, TSeed>(
        SetupDbContext context,
        DbSet<TModel> dbSet,
        string fileName,
        Func<TSeed, IEnumerable<TModel>> map,
        Func<SetupDbContext, DbSet<TModel>, List<TModel>, Task<List<TModel>>>? filterRecordsToAdd = null)
        where TModel : class
    {
        if (filterRecordsToAdd is null && await dbSet.AnyAsync()) return;

        var path = ResolveSeedDataPath(fileName);

        var json = await File.ReadAllTextAsync(path);
        var seedData = JsonSerializer.Deserialize<TSeed>(json, SeedJsonOptions);

        if (seedData == null) return;

        var records = map(seedData).ToList();
        if (records.Count == 0) return;

        List<TModel> recordsToAdd;
        if (filterRecordsToAdd is null) recordsToAdd = records;
        else recordsToAdd = await filterRecordsToAdd(context, dbSet, records);

        if (recordsToAdd.Count == 0) return;

        await dbSet.AddRangeAsync(recordsToAdd);
        await context.SaveChangesAsync();
    }

    private static string ResolveSeedDataPath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var currentDirectory = Directory.GetCurrentDirectory();
        var candidatePaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Data", fileName),
                Path.Combine(currentDirectory, "Data", fileName),
                Path.Combine(currentDirectory, "TripRadar.Server.Db", "Data", fileName),
                Path.Combine(currentDirectory, "TripRadar.Server", "TripRadar.Server.Db", "Data", fileName),
                Path.Combine(currentDirectory, "src", "TripRadar", "TripRadar.Server", "TripRadar.Server.Db", "Data", fileName)
            }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var candidatePath in candidatePaths)
        {
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        throw new FileNotFoundException($"Seed data file '{fileName}' was not found. Checked: {string.Join(", ", candidatePaths)}");
    }
}
