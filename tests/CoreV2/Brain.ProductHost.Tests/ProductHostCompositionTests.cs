using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Brain.Product.Abstractions.Authority;
using Brain.Abstractions.Activities;
using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Operations;
using DigitalBrain.ProductHost.Hosting;
using DigitalBrain.ProductHost.Persistence;
using DigitalBrain.ProductHost.Secrets;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Brain.ProductHost.Tests;

public sealed class ProductHostCompositionTests
{
    private const string PostgreSql = "Host=postgres;Database=digitalbrain;Username=brain;Password=not-used";
    private const string DurableStorage = "UseDevelopmentStorage=true";

    [Theory]
    [InlineData(ProductPersistenceKind.InMemory, ProductAuthorityKind.External, ProductObjectStorageKind.External, ProductOrleansStorageKind.Durable)]
    [InlineData(ProductPersistenceKind.PostgreSql, ProductAuthorityKind.LocalTest, ProductObjectStorageKind.External, ProductOrleansStorageKind.Durable)]
    [InlineData(ProductPersistenceKind.PostgreSql, ProductAuthorityKind.External, ProductObjectStorageKind.InMemory, ProductOrleansStorageKind.Durable)]
    [InlineData(ProductPersistenceKind.PostgreSql, ProductAuthorityKind.External, ProductObjectStorageKind.External, ProductOrleansStorageKind.InMemory)]
    public void Production_rejects_every_in_memory_or_local_runtime(
        ProductPersistenceKind persistence,
        ProductAuthorityKind authority,
        ProductObjectStorageKind objectStorage,
        ProductOrleansStorageKind orleans)
    {
        using var host = BuildHost(
            Environments.Production,
            options => ConfigureValidProduction(options, persistence, authority, objectStorage, orleans));

        Assert.Throws<OptionsValidationException>(() =>
            host.Services.GetRequiredService<IOptions<ProductStoreOptions>>().Value);
    }

    [Theory]
    [InlineData(nameof(ProductStoreOptions.PostgreSqlConnectionString))]
    [InlineData(nameof(ProductStoreOptions.ObjectStoreBucket))]
    [InlineData(nameof(ProductStoreOptions.ObjectStoreEncryptionKeyId))]
    [InlineData(nameof(ProductStoreOptions.OrleansClusteringConnectionString))]
    [InlineData(nameof(ProductStoreOptions.OrleansGrainStorageConnectionString))]
    [InlineData(nameof(ProductStoreOptions.OrleansReminderConnectionString))]
    public void Production_rejects_missing_durable_configuration(string omitted)
    {
        using var host = BuildHost(
            Environments.Production,
            options =>
            {
                ConfigureValidProduction(options);
                typeof(ProductStoreOptions).GetProperty(omitted)!.SetValue(options, null);
            });

        Assert.Throws<OptionsValidationException>(() =>
            host.Services.GetRequiredService<IOptions<ProductStoreOptions>>().Value);
    }

    [Fact]
    public void Production_requires_external_authority_object_store_and_key_encryption_implementations()
    {
        using var host = BuildHost(
            Environments.Production,
            options => ConfigureValidProduction(options),
            registerExternalServices: false);

        var exception = Assert.Throws<OptionsValidationException>(() =>
            host.Services.GetRequiredService<IOptions<ProductStoreOptions>>().Value);

        Assert.Contains(nameof(IBrainAccessAuthority), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IEncryptedSecretObjectStore), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IKeyEncryptionProvider), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_composition_uses_postgresql_and_durable_orleans()
    {
        using var host = BuildHost(
            Environments.Production,
            options => ConfigureValidProduction(options));

        _ = host.Services.GetRequiredService<IOptions<ProductStoreOptions>>().Value;
        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ProductDbContext>().Database;

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", database.ProviderName);
        Assert.NotNull(host.Services.GetRequiredService<IDurableOrleansConfiguration>());
        Assert.Equal(
            "DurableProductOperationGateway",
            scope.ServiceProvider.GetRequiredService<IOperationGateway>().GetType().Name);
        Assert.Equal(
            "DurableProductActivityProjectionService",
            scope.ServiceProvider.GetRequiredService<IProductActivityProjectionService>().GetType().Name);
        Assert.Equal(
            "DurableProductDeliveryService",
            scope.ServiceProvider.GetRequiredService<IDurableProductDeliveryService>().GetType().Name);
    }

    [Fact]
    public void Composition_snapshots_options_before_selecting_production_providers()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production,
        });
        builder.Services.AddSingleton<IBrainAccessAuthority, FixtureAuthority>();
        builder.Services.AddSingleton<IEncryptedSecretObjectStore, RecordingObjectStore>();
        builder.Services.AddSingleton<IKeyEncryptionProvider>(new ReversingKeyEncryptionProvider("key-v1"));
        ProductStoreOptions? retained = null;
        builder.AddDigitalBrainProductHost(options =>
        {
            retained = options;
            ConfigureValidProduction(options);
        });

        using var host = builder.Build();
        _ = host.Services.GetRequiredService<IOptions<ProductStoreOptions>>().Value;
        retained!.Persistence = ProductPersistenceKind.InMemory;
        using var scope = host.Services.CreateScope();

        Assert.Equal(
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            scope.ServiceProvider.GetRequiredService<ProductDbContext>().Database.ProviderName);
    }

#if DEBUG
    [Fact]
    public void Development_can_explicitly_use_deterministic_local_implementations()
    {
        using var host = BuildHost(
            Environments.Development,
            options =>
            {
                options.Persistence = ProductPersistenceKind.InMemory;
                options.Authority = ProductAuthorityKind.LocalTest;
                options.ObjectStorage = ProductObjectStorageKind.InMemory;
                options.OrleansStorage = ProductOrleansStorageKind.InMemory;
                options.ObjectStoreEncryptionKeyId = "development-key";
            },
            registerExternalServices: false);

        _ = host.Services.GetRequiredService<IOptions<ProductStoreOptions>>().Value;
        using var scope = host.Services.CreateScope();

        Assert.Equal(
            "Microsoft.EntityFrameworkCore.InMemory",
            scope.ServiceProvider.GetRequiredService<ProductDbContext>().Database.ProviderName);
        Assert.NotNull(host.Services.GetRequiredService<IBrainAccessAuthority>());
        Assert.NotNull(host.Services.GetRequiredService<ISecretStore>());
    }
#endif

    [Fact]
    public async Task Production_startup_fails_closed_when_reminder_storage_is_missing()
    {
        using var host = BuildHost(
            Environments.Production,
            options =>
            {
                ConfigureValidProduction(options);
                options.OrleansReminderConnectionString = null;
            });

        await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Composition_resolves_and_exercises_durable_core_runtime_services()
    {
        var module = new ModuleId("runtime");
        var role = new NeuronRoleId("runtime.entry");
        var operation = new OperationDescriptor(
            new OperationId("runtime/run@1"),
            new ContractId("runtime/input@1"),
            new ContractId("runtime/result@1"),
            role,
            module,
            new ContractVersion(1));
        var manifest = new ModuleManifest(
            module,
            new ModuleVersion(1, 0, 0),
            [],
            [new NeuronRoleDescriptor(role, NeuronScope.Workspace, module)],
            [operation],
            [],
            [],
            [],
            [],
            []);
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
        });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(manifest);
        builder.Services.AddSingleton(ProductOperationRuntimeRegistration.Create<RuntimeInput, RuntimeResult>(
            operation,
            static input => input.Value,
            static (input, _, _) => Task.FromResult(new ActivityResultReference(
                new ContractId("runtime/result@1"),
                new ActivityPayloadReference($"result/{input.Value}")))));
        builder.Services.AddSingleton<IBrainAccessAuthority, FixtureAuthority>();
        builder.AddDigitalBrainProductHost(options =>
        {
            options.Persistence = ProductPersistenceKind.InMemory;
            options.Authority = ProductAuthorityKind.External;
            options.ObjectStorage = ProductObjectStorageKind.InMemory;
            options.OrleansStorage = ProductOrleansStorageKind.InMemory;
        });
        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IOperationGateway>();
        var projections = scope.ServiceProvider.GetRequiredService<IProductActivityProjectionService>();
        var deliveries = scope.ServiceProvider.GetRequiredService<IDurableProductDeliveryService>();
        var caller = new WorkspaceContext(
            new WorkspaceId("workspace-1"),
            new PrincipalId("principal-1"),
            isServicePrincipal: false);

        var accepted = await gateway.InvokeAsync<RuntimeInput, RuntimeResult>(
            operation,
            new RuntimeInput("42"),
            caller,
            new IdempotencyKey("runtime-42"),
            TestContext.Current.CancellationToken);
        var view = await projections.ObserveAsync(
            accepted.Activity,
            caller,
            TestContext.Current.CancellationToken);
        var firstDelivery = await deliveries.EnqueueAsync(
            new DurableProductDelivery("delivery-42", caller.Workspace, "payload/42"),
            TestContext.Current.CancellationToken);
        var duplicateDelivery = await deliveries.EnqueueAsync(
            new DurableProductDelivery("delivery-42", caller.Workspace, "payload/42"),
            TestContext.Current.CancellationToken);
        await deliveries.CompleteAsync("delivery-42", caller.Workspace, TestContext.Current.CancellationToken);

        Assert.Equal(ActivityStatus.Completed, view.Status);
        Assert.Equal("result/42", view.Result!.Payload.Value);
        Assert.True(firstDelivery);
        Assert.False(duplicateDelivery);
        Assert.True(await deliveries.IsCompletedAsync(
            "delivery-42",
            caller.Workspace,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Core_services_have_no_http_or_secret_store_dependency()
    {
        var forbidden = new[] { typeof(ISecretStore), typeof(HttpContext) };
        var coreAssembly = typeof(Brain.Core.Modules.ModuleRegistry).Assembly;
        var violations = coreAssembly.GetTypes()
            .Where(static type => type.IsClass)
            .SelectMany(static type => type.GetConstructors(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic))
            .SelectMany(constructor => constructor.GetParameters()
                .Where(parameter => forbidden.Any(candidate =>
                    candidate.IsAssignableFrom(parameter.ParameterType)))
                .Select(parameter => $"{constructor.DeclaringType!.FullName} -> {parameter.ParameterType.FullName}"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public async Task Secret_material_is_encrypted_before_object_storage_and_never_logged()
    {
        const string literal = "literal-provider-refresh-token";
        var objectStore = new RecordingObjectStore();
        var logger = new RecordingLogger<EncryptedObjectSecretStore>();
        var store = new EncryptedObjectSecretStore(
            objectStore,
            new ReversingKeyEncryptionProvider("key-v1"),
            Options.Create(new ProductStoreOptions
            {
                ObjectStoreBucket = "test-secrets",
                ObjectStoreEncryptionKeyId = "key-v1",
            }),
            logger);
        var connection = new ConnectionReference("salesforce-primary");
        var material = SecretMaterial.FromUtf8(literal);

        await store.PutAsync(connection, material, TestContext.Current.CancellationToken);

        Assert.NotNull(objectStore.Stored);
        Assert.DoesNotContain(literal, Encoding.UTF8.GetString(objectStore.Stored!.Ciphertext.Span));
        Assert.DoesNotContain(literal, string.Join(Environment.NewLine, logger.Messages));
        var restored = await ((IProviderSecretReader)store).GetAsync(
            connection,
            TestContext.Current.CancellationToken);
        Assert.Equal(literal, Encoding.UTF8.GetString(restored.CopyForProviderBridge().Span));
    }

    [Fact]
    public void Secret_material_text_and_json_are_redacted()
    {
        const string literal = "literal-provider-client-secret";
        var material = SecretMaterial.FromUtf8(literal);

        var text = material.ToString();
        var json = JsonSerializer.Serialize(material);

        Assert.DoesNotContain(literal, text, StringComparison.Ordinal);
        Assert.DoesNotContain(literal, json, StringComparison.Ordinal);
        Assert.Contains("REDACTED", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Product_relational_model_contains_no_secret_material()
    {
        using var host = BuildHost(
            Environments.Production,
            options => ConfigureValidProduction(options));
        using var scope = host.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<ProductDbContext>().Model;

        Assert.DoesNotContain(
            model.GetEntityTypes().SelectMany(static entity => entity.GetProperties()),
            static property => property.ClrType == typeof(SecretMaterial));
    }

    [Fact]
    public void Encrypted_envelope_does_not_expose_mutable_backing_buffers()
    {
        var payload = new EncryptedSecretPayload("key-v1", new byte[] { 1, 2, 3 });
        Assert.True(MemoryMarshal.TryGetArray(payload.Ciphertext, out var exposed));

        exposed.Array![exposed.Offset] = 99;

        Assert.Equal(1, payload.Ciphertext.Span[0]);
    }

    private static IHost BuildHost(
        string environment,
        Action<ProductStoreOptions> configure,
        bool registerExternalServices = true)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment,
        });
        builder.Logging.ClearProviders();
        if (registerExternalServices)
        {
            builder.Services.AddSingleton<IBrainAccessAuthority, FixtureAuthority>();
            builder.Services.AddSingleton<IEncryptedSecretObjectStore, RecordingObjectStore>();
            builder.Services.AddSingleton<IKeyEncryptionProvider>(new ReversingKeyEncryptionProvider("key-v1"));
        }

        builder.AddDigitalBrainProductHost(configure);
        return builder.Build();
    }

    private static void ConfigureValidProduction(
        ProductStoreOptions options,
        ProductPersistenceKind persistence = ProductPersistenceKind.PostgreSql,
        ProductAuthorityKind authority = ProductAuthorityKind.External,
        ProductObjectStorageKind objectStorage = ProductObjectStorageKind.External,
        ProductOrleansStorageKind orleans = ProductOrleansStorageKind.Durable)
    {
        options.Persistence = persistence;
        options.Authority = authority;
        options.ObjectStorage = objectStorage;
        options.OrleansStorage = orleans;
        options.PostgreSqlConnectionString = PostgreSql;
        options.ObjectStoreBucket = "digitalbrain-secrets";
        options.ObjectStoreEncryptionKeyId = "key-v1";
        options.OrleansClusteringConnectionString = DurableStorage;
        options.OrleansGrainStorageConnectionString = DurableStorage;
        options.OrleansReminderConnectionString = DurableStorage;
    }

    private sealed class FixtureAuthority : IBrainAccessAuthority
    {
        public Task<BrainAccessGrant> AuthenticateAsync(
            AuthorityAuthenticationRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Brain.Product.Abstractions.Operations.WorkspacePresentation>>
            GetWorkspacePresentationsAsync(BrainAccessGrant accessGrant, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class ReversingKeyEncryptionProvider(string keyId) : IKeyEncryptionProvider
    {
        public string KeyId { get; } = keyId;

        public ValueTask<EncryptedSecretPayload> EncryptAsync(
            ReadOnlyMemory<byte> plaintext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new EncryptedSecretPayload(
                KeyId,
                plaintext.ToArray().Reverse().ToArray()));
        }

        public ValueTask<ReadOnlyMemory<byte>> DecryptAsync(
            EncryptedSecretPayload payload,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(payload.Ciphertext.ToArray().Reverse().ToArray());
        }
    }

    private sealed class RecordingObjectStore : IEncryptedSecretObjectStore
    {
        public EncryptedSecretPayload? Stored { get; private set; }

        public ValueTask PutAsync(
            string bucket,
            string objectKey,
            EncryptedSecretPayload payload,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stored = payload;
            return ValueTask.CompletedTask;
        }

        public ValueTask<EncryptedSecretPayload> GetAsync(
            string bucket,
            string objectKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Stored ?? throw new KeyNotFoundException());
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private sealed record RuntimeInput(string Value);

    private sealed record RuntimeResult(string Value);
}
