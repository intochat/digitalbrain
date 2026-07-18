using Microsoft.EntityFrameworkCore;
using TripRadar.Server.API.Extensions;
using TripRadar.Server.Application.Extensions;
using TripRadar.Server.Db;
using TripRadar.Server.Infrastructure.Extensions;
using TripRadar.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Host.ConfigureHostBuilder(builder.Configuration, builder.Environment);

builder
    .Services
    .ConfigureApi(builder.Configuration, builder.Environment)
    .ConfigureApplicationLayer()
    .ConfigureInfrastructure(builder.Configuration, builder.Environment);

var app = builder.Build();

if (builder.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var setupDbContext = scope.ServiceProvider.GetRequiredService<SetupDbContext>();

        var pendingMigrations = await setupDbContext.Database.GetPendingMigrationsAsync();
        var migrations = pendingMigrations as string[] ?? pendingMigrations.ToArray();
        if (migrations.Length != 0)
        {
            logger.LogInformation("Applying {Count} pending migrations", migrations.Length);
            await setupDbContext.Database.MigrateAsync();
        }

        await setupDbContext.SeedAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while migrating or seeding the development database");
        throw;
    }
}

app.BuildApplication();

app.Run();
