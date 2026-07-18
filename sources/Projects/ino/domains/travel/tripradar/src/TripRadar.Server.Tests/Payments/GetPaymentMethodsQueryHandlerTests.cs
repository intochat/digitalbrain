using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.Mappings;
using TripRadar.Server.Application.UseCases.Payments.Queries.GetPaymentMethods;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Tests.Payments;

public class GetPaymentMethodsQueryHandlerTests
{
    private readonly Mock<ICurrentUserContext> _currentUserContext = new();
    private readonly Mock<IStripeGateway> _stripeGateway = new();
    private readonly Mock<IUserSubscriptionRepository> _userSubscriptionRepository = new();
    private readonly GetPaymentMethodsQueryHandler _handler;

    public GetPaymentMethodsQueryHandlerTests()
    {
        var configuration = new MapperConfiguration(
            cfg => cfg.AddProfile<PaymentsProfile>(),
            NullLoggerFactory.Instance);
        IMapper mapper = configuration.CreateMapper();
        _handler = new GetPaymentMethodsQueryHandler(
            _stripeGateway.Object,
            mapper,
            _currentUserContext.Object,
            _userSubscriptionRepository.Object);
    }

    [Fact]
    public async Task Handle_UserWithoutLoadedNavigation_UsesRepositorySubscription()
    {
        var user = User.Register("Password123!", "anton@example.com", true);
        var repositorySubscription = new UserSubscription(user);
        repositorySubscription.UpdateStripeCustomerId("cus_repo");

        var paymentMethods = new List<StripePaymentMethodInfo>
        {
            new()
            {
                Id = "pm_1",
                Type = "card",
                Brand = "visa",
                Last4 = "4242",
                ExpMonth = 12,
                ExpYear = 2030,
                CreatedAt = DateTime.UtcNow
            }
        };

        var subscription = new StripeSubscriptionInfo
        {
            DefaultPaymentMethodId = "pm_1",
            Status = "active"
        };

        _currentUserContext.Setup(x => x.GetRequiredUser()).Returns(user);
        _userSubscriptionRepository
            .Setup(x => x.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repositorySubscription);
        _stripeGateway
            .Setup(x => x.GetPaymentMethodsAsync("cus_repo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(paymentMethods);
        _stripeGateway
            .Setup(x => x.GetSubscriptionByCustomerAsync("cus_repo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

        var result = await _handler.Handle(new GetPaymentMethodsQuery("anton"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PaymentMethods.Should().ContainSingle();
        result.Value.PaymentMethods[0].IsDefault.Should().BeTrue();
        _stripeGateway.Verify(x => x.GetPaymentMethodsAsync("cus_repo", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenStripeReturnsDuplicateCards_DeduplicatesAndPreservesDefaultCard()
    {
        var user = User.Register("Password123!", "anton@example.com", true);
        var repositorySubscription = new UserSubscription(user);
        repositorySubscription.UpdateStripeCustomerId("cus_repo");

        var now = DateTime.UtcNow;
        var paymentMethods = new List<StripePaymentMethodInfo>
        {
            new()
            {
                Id = "pm_newest_duplicate",
                Type = "card",
                Brand = "visa",
                Last4 = "4242",
                ExpMonth = 1,
                ExpYear = 2028,
                CreatedAt = now
            },
            new()
            {
                Id = "pm_default_duplicate",
                Type = "card",
                Brand = "visa",
                Last4 = "4242",
                ExpMonth = 1,
                ExpYear = 2028,
                CreatedAt = now.AddMinutes(-10)
            },
            new()
            {
                Id = "pm_mastercard",
                Type = "card",
                Brand = "mastercard",
                Last4 = "3222",
                ExpMonth = 11,
                ExpYear = 2032,
                CreatedAt = now.AddMinutes(-5)
            }
        };

        var subscription = new StripeSubscriptionInfo
        {
            DefaultPaymentMethodId = "pm_default_duplicate",
            Status = "active"
        };

        _currentUserContext.Setup(x => x.GetRequiredUser()).Returns(user);
        _userSubscriptionRepository
            .Setup(x => x.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repositorySubscription);
        _stripeGateway
            .Setup(x => x.GetPaymentMethodsAsync("cus_repo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(paymentMethods);
        _stripeGateway
            .Setup(x => x.GetSubscriptionByCustomerAsync("cus_repo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

        var result = await _handler.Handle(new GetPaymentMethodsQuery("anton"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PaymentMethods.Should().HaveCount(2);

        var visaCard = result.Value.PaymentMethods
            .Single(pm => pm.Card.Brand == "Visa" && pm.Card.Last4 == "4242" && pm.Card.ExpMonth == 1 && pm.Card.ExpYear == 2028);

        visaCard.IsDefault.Should().BeTrue();
        visaCard.Id.Should().Be("pm_default_duplicate");
    }
}
