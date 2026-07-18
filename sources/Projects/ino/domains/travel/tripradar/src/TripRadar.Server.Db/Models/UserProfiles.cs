using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Index(nameof(UsernameHash), IsUnique = true)]
[Index(nameof(EmailHash), IsUnique = true)]
[Index(nameof(TelegramUserId), IsUnique = true)]
[Table(DbConstants.Tables.UserProfiles, Schema = DbConstants.SchemaName)]
public class UserProfiles
{
    [Key] public long UserProfileId { get; set; }

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Numeric.BigInt)]
    public long UserId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.TextType)]
    public string? Username { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar255)]
    public string Password { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.TextType)]
    public string Email { get; set; } = null!;

    [Column(TypeName = "varchar(64)")]
    public string? UsernameHash { get; set; }

    [Column(TypeName = "varchar(64)")]
    public string? EmailHash { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Boolean.BooleanType)]
    public bool IsEmailConfirmed { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar255)]
    public string? EmailConfirmationToken { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime? EmailConfirmationTokenExpiry { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar255)]
    public string? PasswordResetToken { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime? PasswordResetTokenExpiry { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.TextType)]
    public string? FirstName { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.TextType)]
    public string? LastName { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.TextType)]
    public string? PhoneNumber { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.TextType)]
    public string? IpAddress { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar255)]
    public string RefreshToken { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime RefreshTokenExpiryTime { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar64)]
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar255)]
    public string? GoogleId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.BigInt)]
    public long? TelegramUserId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Integer)]
    public int TimezoneId { get; set; } = 1;

    [Column(TypeName = "VARCHAR(500)")]
    public string? ProfilePictureUrl { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Integer)]
    public int? LanguageId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Integer)]
    public int? CountryId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Integer)]
    public int AccessFailedCount { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime? LockoutEnd { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Boolean.BooleanType)]
    public bool LockoutEnabled { get; set; } = true;

    [ForeignKey("UserId")]
    public Users User { get; set; } = null!;

    [ForeignKey("LanguageId")]
    public Languages? Language { get; set; }

    [ForeignKey("CountryId")]
    public Countries? Country { get; set; }

    [ForeignKey("TimezoneId")]
    [DeleteBehavior(DeleteBehavior.Restrict)]
    public Timezones? TimezoneReference { get; set; }
}

