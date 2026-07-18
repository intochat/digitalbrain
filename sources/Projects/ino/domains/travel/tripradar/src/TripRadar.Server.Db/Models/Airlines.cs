using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Index(nameof(AirlineCode), IsUnique = true, Name = "IX_Airlines_AirlineCode")]
[Index(nameof(AirlineName), Name = "IX_Airlines_AirlineName")]
[Table(DbConstants.Tables.Airlines, Schema = DbConstants.SchemaName)]
public class Airlines
{
    [Key]
    public int AirlineId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar32)]
    public string AirlineCode { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar255)]
    public string AirlineName { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.TextType)]
    public string? SearchAliases { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Boolean.BooleanType)]
    public bool IsAlliance { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Boolean.BooleanType)]
    public bool IsActive { get; set; } = true;
}
