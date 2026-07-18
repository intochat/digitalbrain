using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class CurrenciesConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable(DbConstants.Tables.Currencies, DbConstants.SchemaName);

        builder.Property<int>("CurrencyId")
            .HasColumnName("CurrencyId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasKey("CurrencyId");

        builder.Property(c => c.CurrencyCode)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L10);

        builder.Property(c => c.CurrencyName)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100);

        builder.HasIndex(c => c.CurrencyCode)
            .IsUnique()
            .HasDatabaseName("IX_Currencies_CurrencyCode");

        builder.HasIndex(c => c.CurrencyName)
            .HasDatabaseName("IX_Currencies_CurrencyName");
    }
}

