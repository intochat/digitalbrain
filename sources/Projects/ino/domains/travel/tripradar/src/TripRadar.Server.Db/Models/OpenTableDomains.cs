using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.OpenTableDomains, Schema = DbConstants.SchemaName)]
public class OpenTableDomains
{
    [Key] public int OpenTableDomainId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)] public string Domain { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)] public string Country { get; set; } = null!;
}
