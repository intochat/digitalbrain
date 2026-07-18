using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.Locations, Schema = DbConstants.SchemaName)]
public class Locations
{
    [Key] public int LocationId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar24)] public string? RowId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Integer)] public int? GoogleId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Integer)] public int? GoogleParentId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)] public string Name { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar200)] public string CanonicalName { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar2)] public string CountryCode { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar50)] public string TargetType { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Integer)] public int? Reach { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Float)] public double? GpsLongitude { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Numeric.Float)] public double? GpsLatitude { get; set; }
}
