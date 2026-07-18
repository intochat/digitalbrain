using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class FeedbacksConfiguration : IEntityTypeConfiguration<Feedback>
{
    public void Configure(EntityTypeBuilder<Feedback> builder)
    {
        builder.ToTable(DbConstants.Tables.Feedbacks, DbConstants.SchemaName);

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("FeedbackId")
            .ValueGeneratedOnAdd()
            .IsRequired();

        builder.Property(f => f.UserId)
            .IsRequired();

        builder.Property(f => f.Title)
            .HasMaxLength(DbConstants.Validations.MaxLengths.L200)
            .IsRequired();

        builder.Property(f => f.Content)
            .HasMaxLength(DbConstants.Validations.MaxLengths.L2000)
            .IsRequired();

        builder.Property(f => f.Rating)
            .IsRequired();

        builder.Property(f => f.CategoryId)
            .IsRequired();

        builder.Property(f => f.CreatedOn)
            .HasConversion(
                v => v.ToUniversalTime(),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
            .IsRequired();

        builder.Property(f => f.UpdatedOn)
            .HasConversion(
                v => v.HasValue ? v.Value.ToUniversalTime() : (DateTime?)null,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);

        builder.HasOne(f => f.User)
            .WithMany("Feedbacks")
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.Category)
            .WithMany()
            .HasForeignKey(f => f.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(f => f.UserId);
        builder.HasIndex(f => f.CategoryId);
        builder.HasIndex(f => f.CreatedOn);
    }
}
