using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class GoogleLrLanguagesConfiguration : IEntityTypeConfiguration<GoogleLrLanguage>
{
    public void Configure(EntityTypeBuilder<GoogleLrLanguage> builder)
    {
        builder.ToTable(DbConstants.Tables.GoogleLrLanguages, DbConstants.SchemaName);

        builder.Property<int>("GoogleLrLanguageId")
            .HasColumnName("GoogleLrLanguageId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasKey("GoogleLrLanguageId");

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

