using Microsoft.Extensions.Logging.Abstractions;
using TripRadar.Server.Infrastructure.Constants;
using TripRadar.Server.Infrastructure.Services;

namespace TripRadar.Server.Tests.Emails;

public class TranslationServiceTests
{
    private readonly TranslationService _service = new(NullLogger<TranslationService>.Instance);

    [Fact]
    public async Task GetTranslationAsync_EmailConfirmationMainHeading_ReturnsConfiguredText()
    {
        var result = await _service.GetTranslationAsync(
            EmailConstants.DefaultLanguage,
            EmailConstants.Sections.EmailConfirmation,
            EmailConstants.Keys.MainHeading);

        result.Should().Be("Welcome aboard!");
    }

    [Fact]
    public async Task GetTranslationAsync_CommonFooterBestRegards_Russian_ReturnsConfiguredText()
    {
        var result = await _service.GetTranslationAsync(
            "ru",
            EmailConstants.Sections.Common,
            EmailConstants.Keys.FooterBestRegards);

        result.Should().Be("С уважением,");
    }
}
