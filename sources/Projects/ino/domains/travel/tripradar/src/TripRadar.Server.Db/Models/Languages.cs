using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.Languages, Schema = DbConstants.SchemaName)]
public class Languages
{
    [Key] public int LanguageId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar10)] public string LanguageCode { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)] public string LanguageName { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Boolean.BooleanType)] public bool IsInternal { get; set; } = false;
}
