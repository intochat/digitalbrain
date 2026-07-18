using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Index(nameof(TripVaultId), nameof(CreatedOn), IsDescending = new[] { false, true })]
[Table(DbConstants.Tables.TripQueryHistories, Schema = DbConstants.SchemaName)]
public class TripQueryHistories
{
    [Key] public long TripQueryHistoryId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Identifier.Uuid)] public Guid UniqueId { get; set; }

    [Required] public long TripVaultId { get; set; }

    [ForeignKey("TripVaultId")] public TripVaults TripVault { get; set; } = null!;

    [Required] public int ServiceTypeId { get; set; }

    [ForeignKey("ServiceTypeId")] public ServiceTypes ServiceType { get; set; } = null!;

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Json.Jsonb)]
    public string QueryParametersJson { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime? StartDateTime { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime? EndDateTime { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.TextType)]
    public string? ResultSummary { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime CreatedOn { get; set; }
}
