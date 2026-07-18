using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Index(nameof(UniqueId), IsUnique = true)]
[Index(nameof(UserId), nameof(OccurredAt), IsDescending = new[] { false, true })]
[Index(nameof(UserId), nameof(ServiceTypeId), nameof(OccurredAt), IsDescending = new[] { false, false, true })]
[Index(nameof(UserId), nameof(TripVaultId), nameof(OccurredAt), IsDescending = new[] { false, false, true })]
[Index(nameof(UserId), nameof(UsageEventSourceId), nameof(OccurredAt), IsDescending = new[] { false, false, true })]
[Table(DbConstants.Tables.UsageEvents, Schema = DbConstants.SchemaName)]
public class UsageEvents
{
    [Key]
    public long UsageEventId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Identifier.Uuid)]
    public Guid UniqueId { get; set; }

    [Required]
    public long UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public Users User { get; set; } = null!;

    [Required]
    public int ServiceTypeId { get; set; }

    [ForeignKey(nameof(ServiceTypeId))]
    public ServiceTypes ServiceType { get; set; } = null!;

    public long? TripVaultId { get; set; }

    [ForeignKey(nameof(TripVaultId))]
    public TripVaults? TripVault { get; set; }

    [Required]
    public int UsageEventSourceId { get; set; }

    [ForeignKey(nameof(UsageEventSourceId))]
    public UsageEventSources UsageEventSource { get; set; } = null!;

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Decimal10_2)]
    public decimal TokensConsumed { get; set; }

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime OccurredAt { get; set; }

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime CreatedAt { get; set; }
}
