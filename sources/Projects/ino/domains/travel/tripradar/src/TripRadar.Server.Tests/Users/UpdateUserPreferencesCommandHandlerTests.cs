using System.Reflection;
using Microsoft.Extensions.Options;
using Moq;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.Settings;
using TripRadar.Server.Application.UseCases.Users.Commands.UpdateUserPreferences;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;
using ReferenceServiceType = TripRadar.Server.Domain.ReferenceData.ServiceType;
using PreferenceTypeEntity = TripRadar.Server.Domain.Entities.PreferenceType;

namespace TripRadar.Server.Tests.Users;

public class UpdateUserPreferencesCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IUserPreferencesRepository> _userPreferencesRepository = new();
    private readonly Mock<IPreferenceTypeRepository> _preferenceTypeRepository = new();
    private readonly Mock<ICacheService> _cacheService = new();
    private readonly Mock<ICurrentUserContext> _currentUserContext = new();
    private readonly UpdateUserPreferencesCommandHandler _handler;

    public UpdateUserPreferencesCommandHandlerTests()
    {
        _unitOfWork
            .Setup(x => x.StartScopeAsync(It.IsAny<System.Transactions.TransactionScopeOption>(), It.IsAny<System.Transactions.IsolationLevel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UnitOfWorkTransactionScope.Noop());
        _unitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _cacheService
            .Setup(x => x.RemoveAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _handler = new UpdateUserPreferencesCommandHandler(
            _unitOfWork.Object,
            _userPreferencesRepository.Object,
            _preferenceTypeRepository.Object,
            _cacheService.Object,
            Options.Create(new CachingSettings
            {
                Preferences = new PreferencesCacheSettings
                {
                    PreferencesCacheKey = "prefs:{0}"
                }
            }),
            _currentUserContext.Object);
    }

    [Fact]
    public async Task Handle_TrackedPreferenceUpdate_DoesNotCallUpdateRange()
    {
        var user = User.CreateFromTelegramAuth(777, "tester", null, null, null);
        SetPrivateProperty(user, nameof(User.Id), 42L);
        _currentUserContext.Setup(x => x.GetRequiredUser()).Returns(user);

        var existingPreference = new UserPreference(user.Id, 100, "false");
        _userPreferencesRepository
            .Setup(x => x.GetTrackedByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingPreference]);
        _userPreferencesRepository
            .Setup(x => x.AddRangeAsync(It.IsAny<IEnumerable<UserPreference>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _preferenceTypeRepository
            .Setup(x => x.GetActiveByServiceTypeAsync(It.IsAny<ServiceType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ServiceType serviceType, CancellationToken _) =>
                serviceType == ServiceType.Flight
                    ? [CreatePreferenceType(id: 100, serviceTypeId: serviceType.Id, name: nameof(FlightPreferencesDTO.DeepSearch))]
                    : []);

        var command = new UpdateUserPreferencesCommand(
            "tester",
            new UserPreferencePatchRequestDTO
            {
                Flight = new FlightPreferencesDTO
                {
                    DeepSearch = true
                }
            });

        var result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        existingPreference.PreferencesJson.Should().Be("true");
        existingPreference.IsActive.Should().BeTrue();
        _userPreferencesRepository.Verify(
            x => x.UpdateRange(It.IsAny<IEnumerable<UserPreference>>()),
            Times.Never);
        _userPreferencesRepository.Verify(
            x => x.AddRangeAsync(It.IsAny<IEnumerable<UserPreference>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _cacheService.Verify(x => x.RemoveAsync("prefs:tester"), Times.Once);
    }

    private static PreferenceTypeEntity CreatePreferenceType(int id, int serviceTypeId, string name)
    {
        var referenceServiceType = (ReferenceServiceType)Activator.CreateInstance(typeof(ReferenceServiceType), nonPublic: true)!;
        SetPrivateProperty(referenceServiceType, nameof(ReferenceServiceType.Id), serviceTypeId);
        SetPrivateProperty(referenceServiceType, nameof(ReferenceServiceType.Name), ServiceType.Flight.Name);

        var preferenceType = new PreferenceTypeEntity(referenceServiceType, name, PreferenceDataType.Boolean);
        SetPrivateProperty(preferenceType, nameof(PreferenceTypeEntity.Id), id);
        SetPrivateProperty(preferenceType, nameof(PreferenceTypeEntity.ServiceTypeId), serviceTypeId);
        SetPrivateProperty(preferenceType, nameof(PreferenceTypeEntity.CreatedAt), DateTime.UtcNow);
        SetPrivateProperty(preferenceType, nameof(PreferenceTypeEntity.UpdatedAt), DateTime.UtcNow);

        return preferenceType;
    }

    private static void SetPrivateProperty(object target, string propertyName, object value)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var property = target.GetType()
            .GetProperties(flags)
            .FirstOrDefault(p => p.Name == propertyName && p.DeclaringType == target.GetType())
            ?? target.GetType().GetProperties(flags).FirstOrDefault(p => p.Name == propertyName);

        property.Should().NotBeNull($"Property {propertyName} should exist on {target.GetType().Name}");
        property!.SetValue(target, value);
    }
}