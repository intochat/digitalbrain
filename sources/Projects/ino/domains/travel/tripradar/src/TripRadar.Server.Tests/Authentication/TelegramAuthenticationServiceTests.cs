using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Infrastructure.Services;

namespace TripRadar.Server.Tests.Authentication;

public class TelegramAuthenticationServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IUserMonthlyTokenCountRepository> _monthlyTokenCountRepo = new();
    private readonly TelegramAuthenticationService _service;

    private static readonly TelegramAuthDataDTO AuthData = new()
    {
        Id = 99999,
        FirstName = "TgUser",
        Username = "tguser",
        AuthDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        Hash = "hash"
    };

    public TelegramAuthenticationServiceTests()
    {
        _service = new TelegramAuthenticationService(
            _unitOfWork.Object,
            _monthlyTokenCountRepo.Object,
            NullLogger<TelegramAuthenticationService>.Instance);
    }

    [Fact]
    public async Task UpsertUserAsync_DoesNotOpenOwnTransactionScope()
    {
        var existingUser = User.CreateFromTelegramAuth(99999, "tguser", "TgUser", null, null);
        _unitOfWork.Setup(u => u.UserRepository.GetAuthByTelegramUserIdAsync(99999, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        await _service.UpsertUserAsync(AuthData, TestContext.Current.CancellationToken);

        // Service must NOT call StartScopeAsync — the caller manages the scope
        _unitOfWork.Verify(
            u => u.StartScopeAsync(
                It.IsAny<System.Transactions.TransactionScopeOption>(),
                It.IsAny<System.Transactions.IsolationLevel>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpsertUserAsync_ExistingUser_ReturnsWithoutCreating()
    {
        var existingUser = User.CreateFromTelegramAuth(99999, "tguser", "TgUser", null, null);
        _unitOfWork.Setup(u => u.UserRepository.GetAuthByTelegramUserIdAsync(99999, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        var result = await _service.UpsertUserAsync(AuthData, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(existingUser);
        _unitOfWork.Verify(u => u.UserRepository.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpsertUserAsync_NewUser_CreatesAndSavesWithinCallerScope()
    {
        _unitOfWork.Setup(u => u.UserRepository.GetAuthByTelegramUserIdAsync(99999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        _unitOfWork.Setup(u => u.UserRepository.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _monthlyTokenCountRepo
            .Setup(r => r.CreateMonthlyTokenCountsAsync(It.IsAny<User>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _service.UpsertUserAsync(AuthData, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        _unitOfWork.Verify(u => u.UserRepository.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        // No scope opened by the service
        _unitOfWork.Verify(
            u => u.StartScopeAsync(
                It.IsAny<System.Transactions.TransactionScopeOption>(),
                It.IsAny<System.Transactions.IsolationLevel>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
