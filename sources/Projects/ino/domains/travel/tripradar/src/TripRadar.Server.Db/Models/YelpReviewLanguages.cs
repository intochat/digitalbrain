using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.YelpReviewLanguages, Schema = DbConstants.SchemaName)]
public class YelpReviewLanguages
{
    [Key] public int YelpReviewLanguageId { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar10)] public string LanguageCode { get; set; } = null!;

    [Column(TypeName = DbConstants.ColumnTypes.Text.Varchar100)] public string LanguageName { get; set; } = null!;
}
