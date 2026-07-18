using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.TripAdvisorDomains, Schema = DbConstants.SchemaName)]
public class TripAdvisorDomains
{
    [Key] public int TripAdvisorDomainId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)] public string Domain { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)] public string Title { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar50)] public string Locale { get; set; } = null!;
}
