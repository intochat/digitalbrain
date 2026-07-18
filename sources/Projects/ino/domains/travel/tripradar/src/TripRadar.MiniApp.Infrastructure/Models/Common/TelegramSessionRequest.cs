using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common
{
    public sealed record TelegramSessionRequest(
        [property: JsonPropertyName("initData")] string InitData
    );
}