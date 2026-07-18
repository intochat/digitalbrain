using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class CreateScheduledEventQueryRequest
{
    [JsonPropertyName("searchQuery")]
    [DataMember(Name = "searchQuery")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Search query is required.")]
    public string SearchQuery { get; set; } = null!;

    [JsonPropertyName("location")]
    [DataMember(Name = "location")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Location is required.")]
    public string Location { get; set; } = null!;

    [JsonPropertyName("startDate")]
    [DataMember(Name = "startDate")]
    [DataType(DataType.Date)]
    public DateTime? StartDate { get; set; }

    [JsonPropertyName("endDate")]
    [DataMember(Name = "endDate")]
    [DataType(DataType.Date)]
    public DateTime? EndDate { get; set; }

    [JsonPropertyName("additionalParameters")]
    [DataMember(Name = "additionalParameters")]
    public Dictionary<string, object>? AdditionalParameters { get; set; }

    [JsonPropertyName("selectedColumns")]
    [DataMember(Name = "selectedColumns")]
    public IList<QueryColumn>? SelectedColumns { get; set; }

    [JsonPropertyName("nextExecutionTime")]
    [DataMember(Name = "nextExecutionTime")]
    [DataType(DataType.DateTime)]
    [Required(ErrorMessage = "NextExecutionTime is required.")]
    public DateTime NextExecutionTime { get; set; }

    [JsonPropertyName("schedule")]
    [DataMember(Name = "schedule")]
    [Required(ErrorMessage = "Schedule is required.")]
    public string Schedule { get; set; } = null!;
}
