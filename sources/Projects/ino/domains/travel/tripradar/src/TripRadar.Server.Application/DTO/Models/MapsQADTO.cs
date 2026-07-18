using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsQADTO
{
    [JsonPropertyName("question")]
    public MapsQuestionDTO? Question { get; set; }

    [JsonPropertyName("answer")]
    public MapsAnswerDTO? Answer { get; set; }

    [JsonPropertyName("total_answers")]
    public int? TotalAnswers { get; set; }
}
