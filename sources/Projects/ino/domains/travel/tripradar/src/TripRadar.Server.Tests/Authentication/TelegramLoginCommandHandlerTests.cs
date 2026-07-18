using MediatR;
using Moq;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.UseCases.Authentication.Commands.GetTokenByTelegramUserId;
using TripRadar.Server.Application.UseCases.Authentication.Commands.TelegramLogin;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Tests.Authentication;

public class TelegramLoginCommandHandlerTests
{
    private readonly Mock<ITelegramAuthValidationService> _authValidation = new();
    private readonly Mock<ISender> _sender = new();
    private readonly TelegramLoginCommandHandler _handler;

    private static readonly TelegramAuthDataDTO ValidAuthData = new()
    {
        Id = 12345,
        FirstName = "Test",
        Username = "testuser",
        AuthDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        Hash = "fakehash"
    };

    public TelegramLoginCommandHandlerTests()
    {
        _handler = new TelegramLoginCommandHandler(_authValidation.Object, _sender.Object);
    }

    [Fact]
    public async Task Handle_InvalidTelegramAuth_ReturnsFailure()
    {
        _authValidation.Setup(v => v.Validate(It.IsAny<TelegramAuthDataDTO>())).Returns(false);

        var result = await _handler.Handle(new TelegramLoginCommand(ValidAuthData), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(Errors.TelegramAuthInvalid.Code);
        _sender.Verify(
            s => s.Send(It.IsAny<GetTokenByTelegramUserIdCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidTelegramId_ReturnsFailure()
    {
        _authValidation.Setup(v => v.Validate(It.IsAny<TelegramAuthDataDTO>())).Returns(true);
        var authDataWithZeroId = new TelegramAuthDataDTO { Id = 0, FirstName = "Test", Hash = "hash" };

        var result = await _handler.Handle(new TelegramLoginCommand(authDataWithZeroId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(Errors.TelegramAuthInvalid.Code);
        _sender.Verify(
            s => s.Send(It.IsAny<GetTokenByTelegramUserIdCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ValidTelegramAuth_RequestsTokensForTelegramUserId()
    {
        var expectedTokens = new AuthenticationModel { Token = "jwt-token", RefreshToken = "refresh-token" };

        _authValidation.Setup(v => v.Validate(ValidAuthData)).Returns(true);
        _sender
            .Setup(s => s.Send(It.IsAny<GetTokenByTelegramUserIdCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(expectedTokens));

        var result = await _handler.Handle(new TelegramLoginCommand(ValidAuthData), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(expectedTokens);
        _sender.Verify(
            s => s.Send(
                It.Is<GetTokenByTelegramUserIdCommand>(command => command.TelegramUserId == ValidAuthData.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_TokenRequestFails_ReturnsSenderFailure()
    {
        _authValidation.Setup(v => v.Validate(ValidAuthData)).Returns(true);
        _sender
            .Setup(s => s.Send(It.IsAny<GetTokenByTelegramUserIdCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AuthenticationModel>(Errors.InternalServerError));

        var result = await _handler.Handle(new TelegramLoginCommand(ValidAuthData), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(Errors.InternalServerError.Code);
    }
}
