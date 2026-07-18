using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class LanguagesConfiguration : IEntityTypeConfiguration<Language>
{
    public void Configure(EntityTypeBuilder<Language> builder)
    {
        builder.ToTable(DbConstants.Tables.Languages, DbConstants.SchemaName);

        builder.Property<int>("LanguageId")
            .HasColumnName("LanguageId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasKey("LanguageId");

        builder.Property(l => l.LanguageCode)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L10);

        builder.Property(l => l.LanguageName)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100);

        builder.Property(u => u.IsInternal)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Boolean.BooleanType)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(l => l.LanguageCode)
            .IsUnique()
            .HasDatabaseName("IX_Languages_LanguageCode");

        builder.HasIndex(l => l.LanguageName)
            .HasDatabaseName("IX_Languages_LanguageName");

        builder.HasIndex(e => e.IsInternal)
            .HasDatabaseName("IX_Languages_IsInternal");
    }
}

