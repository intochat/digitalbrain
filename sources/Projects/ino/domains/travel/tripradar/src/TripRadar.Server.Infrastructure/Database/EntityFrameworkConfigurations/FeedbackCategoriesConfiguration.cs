using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class FeedbackCategoriesConfiguration : IEntityTypeConfiguration<FeedbackCategory>
{
    public void Configure(EntityTypeBuilder<FeedbackCategory> builder)
    {
        builder.ToTable(DbConstants.Tables.FeedbackCategories, DbConstants.SchemaName);

        builder.Property<int>("FeedbackCategoryId")
            .HasColumnName("FeedbackCategoryId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasKey("FeedbackCategoryId");

        builder.Property(fc => fc.Name)
            .HasMaxLength(DbConstants.Validations.MaxLengths.L50)
            .IsRequired();

        builder.HasIndex(fc => fc.Name).IsUnique();
    }
}

