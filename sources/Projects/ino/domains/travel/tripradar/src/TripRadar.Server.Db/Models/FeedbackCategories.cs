using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TripRadar.Server.Db.Constants;

namespace TripRadar.Server.Db.Models;

[Table(DbConstants.Tables.FeedbackCategories, Schema = DbConstants.SchemaName)]
public class FeedbackCategories
{
    [Key] public int FeedbackCategoryId { get; set; }

    [Required] [MaxLength(50)] public string Name { get; set; } = null!;

    // Navigation property
    public virtual ICollection<Feedbacks> Feedbacks { get; set; } = new List<Feedbacks>();
}
