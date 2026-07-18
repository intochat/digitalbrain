using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class AirportsConfiguration : IEntityTypeConfiguration<Airport>
{
    public void Configure(EntityTypeBuilder<Airport> builder)
    {
        builder.ToTable(DbConstants.Tables.Airports, DbConstants.SchemaName);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("AirportId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(a => a.Code)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L3)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(a => a.City)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(a => a.Country)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(a => a.Latitude)
            .IsRequired(false)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(a => a.Longitude)
            .IsRequired(false)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(a => a.AirportType)
            .IsRequired(false)
            .HasMaxLength(DbConstants.Validations.MaxLengths.L50)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(a => a.SearchAliases)
            .HasColumnType(DbConstants.ColumnTypes.Text.TextType)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(a => a.Code)
            .IsUnique();
    }
}
