using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.Airports, Schema = DbConstants.SchemaName)]
public class Airports
{
    [Key] public int AirportId { get; set; }

    [JsonPropertyName("iata_code")]
    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar3)] public string Code { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)] public string Name { get; set; } = null!;

    [JsonPropertyName("municipality")]
    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)] public string City { get; set; } = null!;

    [JsonPropertyName("iso_country")]
    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)] public string Country { get; set; } = null!;

    [JsonPropertyName("latitude_deg")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude_deg")]
    public double? Longitude { get; set; }

    [JsonPropertyName("type")]
    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar50)] public string? AirportType { get; set; }

    [JsonPropertyName("keywords")]
    [Column(TypeName = DbConstants.ColumnTypes.Text.TextType)] public string? SearchAliases { get; set; }

    [NotMapped]
    public ICollection<ScheduledFlightQueries> DepartureScheduledFlightQueries { get; set; } =
        new List<ScheduledFlightQueries>();

    [NotMapped]
    public ICollection<ScheduledFlightQueries> DestinationScheduledFlightQueries { get; set; } =
        new List<ScheduledFlightQueries>();
}
