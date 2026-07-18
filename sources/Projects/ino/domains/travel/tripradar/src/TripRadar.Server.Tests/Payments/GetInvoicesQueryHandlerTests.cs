using Moq;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.UseCases.Payments.Queries.GetInvoices;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Tests.Payments;

public class GetInvoicesQueryHandlerTests
{
    private readonly Mock<ICurrentUserContext> _currentUserContext = new();
    private readonly Mock<IUserSubscriptionRepository> _userSubscriptionRepository = new();
    private readonly Mock<IStripeGateway> _stripeGateway = new();
    private readonly GetInvoicesQueryHandler _handler;

    public GetInvoicesQueryHandlerTests()
    {
        _handler = new GetInvoicesQueryHandler(
            _stripeGateway.Object,
            _currentUserContext.Object,
            _userSubscriptionRepository.Object);
    }

    [Fact]
    public async Task Handle_StatusNotProvided_DefaultsToPaid()
    {
        var user = CreateUserWithStripeCustomer("cus_123");
        var invoices = new InvoicesDTO { Invoices = [], HasMore = false };

        _currentUserContext.Setup(x => x.GetRequiredUser()).Returns(user);
        _userSubscriptionRepository
            .Setup(x => x.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user.UserSubscription);
        _stripeGateway
            .Setup(x => x.GetInvoicesAsync("cus_123", 20, null, "paid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoices);

        var result = await _handler.Handle(new GetInvoicesQuery("anton"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("paid");
        _stripeGateway.Verify(
            x => x.GetInvoicesAsync("cus_123", 20, null, "paid", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_StatusProvided_UsesExplicitStatus()
    {
        var user = CreateUserWithStripeCustomer("cus_123");
        var invoices = new InvoicesDTO { Invoices = [], HasMore = false };

        _currentUserContext.Setup(x => x.GetRequiredUser()).Returns(user);
        _userSubscriptionRepository
            .Setup(x => x.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserSubscription?)user.UserSubscription);
        _stripeGateway
            .Setup(x => x.GetInvoicesAsync("cus_123", 20, null, "void", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoices);

        var result = await _handler.Handle(new GetInvoicesQuery("anton", Status: "void"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("void");
        _stripeGateway.Verify(
            x => x.GetInvoicesAsync("cus_123", 20, null, "void", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UserWithoutStripeCustomer_ReturnsEmptyResultWithPaidStatus()
    {
        var user = User.Register("Password123!", "anton@example.com", true);

        _currentUserContext.Setup(x => x.GetRequiredUser()).Returns(user);
        _userSubscriptionRepository
            .Setup(x => x.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserSubscription?)null);

        var result = await _handler.Handle(new GetInvoicesQuery("anton"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Invoices.Should().BeEmpty();
        result.Value!.Status.Should().Be("paid");
        _stripeGateway.Verify(
            x => x.GetInvoicesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_UserWithoutLoadedNavigation_UsesRepositorySubscription()
    {
        var user = User.Register("Password123!", "anton@example.com", true);
        var repositorySubscription = new UserSubscription(user);
        repositorySubscription.UpdateStripeCustomerId("cus_repo");
        var invoices = new InvoicesDTO { Invoices = [], HasMore = false };

        _currentUserContext.Setup(x => x.GetRequiredUser()).Returns(user);
        _userSubscriptionRepository
            .Setup(x => x.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repositorySubscription);
        _stripeGateway
            .Setup(x => x.GetInvoicesAsync("cus_repo", 20, null, "paid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoices);

        var result = await _handler.Handle(new GetInvoicesQuery("anton"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _stripeGateway.Verify(
            x => x.GetInvoicesAsync("cus_repo", 20, null, "paid", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static User CreateUserWithStripeCustomer(string customerId)
    {
        var user = User.Register("Password123!", "anton@example.com", true);
        var subscription = new UserSubscription(user);
        subscription.UpdateStripeCustomerId(customerId);
        return user;
    }
}


