using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class CountriesConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable(DbConstants.Tables.Countries, DbConstants.SchemaName);

        builder.Property<int>("CountryId")
            .HasColumnName("CountryId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasKey("CountryId");

        builder.Property(c => c.CountryName)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(c => c.CountryCode)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L2)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(a => a.CountryCode)
            .IsUnique();
    }
}
