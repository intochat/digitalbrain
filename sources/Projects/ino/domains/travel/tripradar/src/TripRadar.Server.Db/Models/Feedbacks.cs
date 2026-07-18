using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.Feedbacks, Schema = DbConstants.SchemaName)]
public class Feedbacks
{
    [Key] public long FeedbackId { get; set; }

    [Required] public long UserId { get; set; }

    [Required] [StringLength(200)] public string Title { get; set; } = null!;

    [Required] [StringLength(2000)] public string Content { get; set; } = null!;

    [Required] [Range(1, 5)] public int Rating { get; set; }

    [Required] public int CategoryId { get; set; }

    [Required]
    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime CreatedOn { get; set; }

    [Column(TypeName = DbConstants.ColumnTypes.DateTime.TimestampTz)]
    public DateTime? UpdatedOn { get; set; }

    [ForeignKey("UserId")] public Users User { get; set; } = null!;

    [ForeignKey("CategoryId")] public FeedbackCategories Category { get; set; } = null!;
}
