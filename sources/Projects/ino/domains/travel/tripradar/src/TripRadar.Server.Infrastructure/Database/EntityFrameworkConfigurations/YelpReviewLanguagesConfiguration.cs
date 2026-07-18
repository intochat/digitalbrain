using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class YelpReviewLanguagesConfiguration : IEntityTypeConfiguration<YelpReviewLanguage>
{
    public void Configure(EntityTypeBuilder<YelpReviewLanguage> builder)
    {
        builder.ToTable(DbConstants.Tables.YelpReviewLanguages, DbConstants.SchemaName);

        builder.Property<int>("YelpReviewLanguageId")
            .HasColumnName("YelpReviewLanguageId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasKey("YelpReviewLanguageId");

        builder.Property(d => d.LanguageCode)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L10)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(d => d.LanguageName)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(d => d.LanguageCode)
            .IsUnique();
    }
}

