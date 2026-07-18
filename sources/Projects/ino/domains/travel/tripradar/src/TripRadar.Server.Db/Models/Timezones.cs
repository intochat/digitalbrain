using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Index(nameof(TimezoneCode), IsUnique = true, Name = "IX_Timezones_TimezoneCode")]
[Index(nameof(TimezoneName), Name = "IX_Timezones_TimezoneName")]
[Table(DbConstants.Tables.Timezones, Schema = DbConstants.SchemaName)]
public class Timezones
{
    [Key]
    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Integer)]
    public int TimezoneId { get; set; }

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)]
    public string TimezoneCode { get; set; } = null!;

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)]
    public string TimezoneName { get; set; } = null!;
}
