using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.Users, Schema = DbConstants.SchemaName)]
public class Users
{
    [Key] public long UserId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Boolean.BooleanType)] public bool IsActive { get; set; }

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Integer)] public int TierId { get; set; } = 1;

    [ForeignKey("TierId")] public Tiers Tier { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Boolean.BooleanType)] public bool HasDataStorageConsent { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Boolean.BooleanType)]
    [DefaultValue(false)]
    public bool AllowsMarketingEmails { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime CreatedOn { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)] public DateTime? UpdatedOn { get; set; }

    public long? PromoCodeId { get; set; }

    [ForeignKey("PromoCodeId")] public PromoCodes? PromoCode { get; set; }

    [NotMapped]
    public ICollection<UserMonthlyTokenCounts> MonthlyTokenCounts { get; set; } = new List<UserMonthlyTokenCounts>();

    [NotMapped]
    public ICollection<ScheduledFlightQueries> ScheduledFlightQueries { get; set; } = new List<ScheduledFlightQueries>();

    [NotMapped]
    public ICollection<ScheduledHotelQueries> ScheduledHotelQueries { get; set; } = new List<ScheduledHotelQueries>();

    [NotMapped]
    public ICollection<ScheduledEventQueries> ScheduledEventQueries { get; set; } = new List<ScheduledEventQueries>();

    [NotMapped]
    public ICollection<ScheduledLocalPlacesQueries> ScheduledLocalPlacesQueries { get; set; } = new List<ScheduledLocalPlacesQueries>();

    [NotMapped] public ICollection<Feedbacks> Feedbacks { get; set; } = new List<Feedbacks>();

    [NotMapped] public UserSubscriptions? UserSubscription { get; set; }

    [NotMapped] public UserProfiles Profiles { get; set; } = null!;

    [NotMapped] public ICollection<TripVaults> TripVaults { get; set; } = new List<TripVaults>();
}
