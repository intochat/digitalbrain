using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.YelpDomains, Schema = DbConstants.SchemaName)]
public class YelpDomains
{
    [Key] public int YelpDomainId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)] public string Domain { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)] public string Locale { get; set; } = null!;
}
