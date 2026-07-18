using TripRadar.Server.Infrastructure.Services;

namespace TripRadar.Server.Tests.Airports;

public class CityTranslationProviderTests
{
    private readonly CityTranslationProvider _provider = new();

    [Theory]
    [InlineData("Москва", "Moscow")]
    [InlineData("Лондон", "London")]
    [InlineData("Париж", "Paris")]
    [InlineData("Нью-Йорк", "New York")]
    [InlineData("Санкт-Петербург", "Saint Petersburg")]
    [InlineData("Стамбул", "Istanbul")]
    [InlineData("Дубай", "Dubai")]
    [InlineData("Токио", "Tokyo")]
    [InlineData("Берлин", "Berlin")]
    [InlineData("Рим", "Rome")]
    public void GetEnglishCityName_KnownRussianCity_ReturnsEnglish(string russian, string expectedEnglish)
    {
        _provider.GetEnglishCityName(russian).Should().Be(expectedEnglish);
    }

    [Fact]
    public void GetEnglishCityName_UnknownCity_ReturnsNull()
    {
        _provider.GetEnglishCityName("НеизвестныйГород").Should().BeNull();
    }

    [Fact]
    public void GetEnglishCityName_IsCaseInsensitive()
    {
        _provider.GetEnglishCityName("москва").Should().Be("Moscow");
        _provider.GetEnglishCityName("МОСКВА").Should().Be("Moscow");
    }

    [Fact]
    public void GetEnglishCityName_TrimsWhitespace()
    {
        _provider.GetEnglishCityName("  Москва  ").Should().Be("Moscow");
    }
}
