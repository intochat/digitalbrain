using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TripRadar.Server.API.Contracts;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Contracts.Responses.Create;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.API.Extensions;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.Services;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Policies;

namespace TripRadar.Server.Tests.Authentication;

public class DevelopmentAuthEndpointsTests
{
    [Fact]
    public async Task PostDevToken_InDevelopment_ReturnsTokenPair()
    {
        await using var harness = await TestHarness.CreateAsync("Development");

        var response = await harness.Client.PostAsJsonAsync("/api/v1/tokens/dev", new CreateDevLoginRequest(100001), cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<GetLoginResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.Should().NotBeNull();
        payload!.Token.Should().Be("dev-token");
        payload.RefreshToken.Should().Be("dev-refresh");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("basic")]
    [InlineData("BaSiC")]
    public async Task PostDevToken_BasicVariants_RemovePaidEligibility(string? tier)
    {
        await using var harness = await TestHarness.CreateAsync("Development");

        var response = await harness.Client.PostAsJsonAsync("/api/v1/tokens/dev", new CreateDevLoginRequest(100001, tier), cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = harness.TelegramAuthenticationService.GetUser(100001);
        user.TierId.Should().Be(Domain.Enums.UserTierType.Basic.Id);
        PaidTierEligibilityPolicy.IsEligibleForPaidFeatures(user).Should().BeFalse();
    }

    [Theory]
    [InlineData("essential", 100002)]
    [InlineData("advanced", 100003)]
    public async Task PostDevToken_PaidTiers_EnablePaidEligibility(string tier, long telegramUserId)
    {
        await using var harness = await TestHarness.CreateAsync("Development");

        var response = await harness.Client.PostAsJsonAsync("/api/v1/tokens/dev", new CreateDevLoginRequest(telegramUserId, tier), cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = harness.TelegramAuthenticationService.GetUser(telegramUserId);
        PaidTierEligibilityPolicy.IsEligibleForPaidFeatures(user).Should().BeTrue();
        user.UserSubscription.Should().NotBeNull();
        user.UserSubscription!.IsActive.Should().BeTrue();
        user.UserSubscription.SubscriptionExpirationTime.Should().BeNull();
    }

    [Fact]
    public async Task PostDevToken_InvalidTier_ReturnsBadRequest()
    {
        await using var harness = await TestHarness.CreateAsync("Development");

        var response = await harness.Client.PostAsJsonAsync("/api/v1/tokens/dev", new CreateDevLoginRequest(100001, "vip"), cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostDevToken_SameUserCanSwitchBetweenTiers()
    {
        await using var harness = await TestHarness.CreateAsync("Development");

        (await harness.Client.PostAsJsonAsync("/api/v1/tokens/dev", new CreateDevLoginRequest(100050, "advanced"), cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var user = harness.TelegramAuthenticationService.GetUser(100050);
        user.TierId.Should().Be(Domain.Enums.UserTierType.Advanced.Id);
        PaidTierEligibilityPolicy.IsEligibleForPaidFeatures(user).Should().BeTrue();

        (await harness.Client.PostAsJsonAsync("/api/v1/tokens/dev", new CreateDevLoginRequest(100050, "basic"), cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        user.TierId.Should().Be(Domain.Enums.UserTierType.Basic.Id);
        PaidTierEligibilityPolicy.IsEligibleForPaidFeatures(user).Should().BeFalse();
        user.UserSubscription.Should().NotBeNull();
        user.UserSubscription!.IsActive.Should().BeFalse();

        (await harness.Client.PostAsJsonAsync("/api/v1/tokens/dev", new CreateDevLoginRequest(100050, "essential"), cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        user.TierId.Should().Be(Domain.Enums.UserTierType.Essential.Id);
        PaidTierEligibilityPolicy.IsEligibleForPaidFeatures(user).Should().BeTrue();
        user.UserSubscription!.IsActive.Should().BeTrue();
        user.UserSubscription.SubscriptionExpirationTime.Should().BeNull();
    }

    [Fact]
    public async Task PostDevToken_InProduction_RouteIsNotRegistered()
    {
        await using var harness = await TestHarness.CreateAsync("Production");

        var response = await harness.Client.PostAsJsonAsync("/api/v1/tokens/dev", new CreateDevLoginRequest(100001, null), cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OpenApi_DoesNotContainDevTokenRoute()
    {
        await using var harness = await TestHarness.CreateAsync("Development", mapOpenApi: true);

        var response = await harness.Client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var document = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        document.Should().NotContain("/api/v1/tokens/dev");
    }

    private sealed class TestHarness : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private TestHarness(WebApplication app, HttpClient client, FakeTelegramAuthenticationService telegramAuthenticationService)
        {
            _app = app;
            Client = client;
            TelegramAuthenticationService = telegramAuthenticationService;
        }

        public HttpClient Client { get; }
        public FakeTelegramAuthenticationService TelegramAuthenticationService { get; }

        public static async Task<TestHarness> CreateAsync(string environmentName, bool mapOpenApi = false)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = environmentName
            });

            builder.WebHost.UseTestServer();
            builder.Services.AddOpenApi();

            var telegramAuthenticationService = new FakeTelegramAuthenticationService();
            var tokenIssuer = new FakeTokenIssuer();
            var authResponseBuilder = new FakeAuthResponseBuilder();
            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork
                .Setup(x => x.StartScopeAsync(
                    It.IsAny<System.Transactions.TransactionScopeOption>(),
                    It.IsAny<System.Transactions.IsolationLevel>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(UnitOfWorkTransactionScope.Noop());

            builder.Services.AddSingleton<ITelegramAuthenticationService>(telegramAuthenticationService);
            builder.Services.AddSingleton<IAuthenticationTokenIssuer>(tokenIssuer);
            builder.Services.AddSingleton<IAuthResponseBuilder>(authResponseBuilder);
            builder.Services.AddSingleton<IUserAccessValidator, UserAccessValidator>();
            builder.Services.AddSingleton(unitOfWork.Object);

            var app = builder.Build();
            if (mapOpenApi)
            {
                app.MapOpenApi();
            }

            app.MapDevelopmentAuthEndpoints();

            await app.StartAsync();
            return new TestHarness(app, app.GetTestClient(), telegramAuthenticationService);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }

    private sealed class FakeTelegramAuthenticationService : ITelegramAuthenticationService
    {
        private readonly Dictionary<long, User> _users = [];

        public Task<Result<User>> UpsertUserAsync(TelegramAuthDataDTO authData, CancellationToken ct = default)
        {
            if (!_users.TryGetValue(authData.Id, out var user))
            {
                user = User.CreateFromTelegramAuth(authData.Id, authData.Username, authData.FirstName, authData.LastName, authData.PhotoUrl);
                _users[authData.Id] = user;
            }

            return Task.FromResult(Result.Success(user));
        }

        public User GetUser(long telegramUserId) => _users[telegramUserId];
    }

    private sealed class FakeTokenIssuer : IAuthenticationTokenIssuer
    {
        public Task<Result<AuthenticationModel>> IssueTokensAsync(User user, UnitOfWorkTransactionScope scope)
        {
            return Task.FromResult(Result.Success(new AuthenticationModel
            {
                Token = "dev-token",
                RefreshToken = "dev-refresh"
            }));
        }
    }

    private sealed class FakeAuthResponseBuilder : IAuthResponseBuilder
    {
        public GetLoginResponse BuildLoginResponse(HttpContext httpContext, string? token, string? refreshToken) => new()
        {
            Token = token,
            RefreshToken = refreshToken
        };

        public ActivateUserResponse BuildActivationResponse(HttpContext httpContext, string? token, string? refreshToken, string email, string username) => new()
        {
            Token = token,
            RefreshToken = refreshToken,
            Email = email,
            Username = username
        };
    }
}
