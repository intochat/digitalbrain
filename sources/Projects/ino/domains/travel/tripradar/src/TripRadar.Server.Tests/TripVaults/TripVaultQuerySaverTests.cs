using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Infrastructure.Services;

namespace TripRadar.Server.Tests.TripVaults;

public class TripVaultQuerySaverTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ITripVaultRepository> _tripVaultRepository = new();
    private readonly TripVaultQuerySaver _service;

    public TripVaultQuerySaverTests()
    {
        _unitOfWork.SetupGet(x => x.TripVaultRepository).Returns(_tripVaultRepository.Object);
        _service = new TripVaultQuerySaver(_unitOfWork.Object, NullLogger<TripVaultQuerySaver>.Instance);
    }

    [Fact]
    public async Task TrySaveSerializedQueryAsync_InactiveVault_SavesHistoryInRequestedVault()
    {
        var inactiveVault = new TripVault(
            ownerId: 123,
            name: "Paris",
            startDate: DateTime.UtcNow.AddDays(-10),
            endDate: DateTime.UtcNow.AddDays(-5));

        _tripVaultRepository
            .Setup(x => x.GetByUniqueIdForUpdateAsync(inactiveVault.UniqueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactiveVault);
        _unitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _service.TrySaveSerializedQueryAsync(
            inactiveVault.UniqueId,
            serviceTypeId: 1,
            queryParametersJson: "{\"query\":\"rome\"}",
            resultSummary: "summary",
            cancellationToken: TestContext.Current.CancellationToken);

        inactiveVault.QueryHistory.Should().ContainSingle();
        inactiveVault.QueryHistory.Single().TripVault.Should().Be(inactiveVault);
        _tripVaultRepository.Verify(
            x => x.GetByOwnerIdAndNameAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _tripVaultRepository.Verify(
            x => x.CreateAsync(It.IsAny<TripVault>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
