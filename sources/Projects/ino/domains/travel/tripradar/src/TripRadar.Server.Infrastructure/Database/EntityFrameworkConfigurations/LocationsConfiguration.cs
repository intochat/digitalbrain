using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class LocationsConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable(DbConstants.Tables.Locations, DbConstants.SchemaName);

        builder.Property<int>("LocationId")
            .HasColumnName("LocationId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasKey("LocationId");

        builder.Property(x => x.RowId)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar24);

        builder.Property(x => x.GoogleId)
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer);

        builder.Property(x => x.GoogleParentId)
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer);

        builder.Property(x => x.Name)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar100)
            .IsRequired();

        builder.Property(x => x.CanonicalName)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar200)
            .IsRequired();

        builder.Property(x => x.CountryCode)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar2)
            .IsRequired();

        builder.Property(x => x.TargetType)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar50)
            .IsRequired();

        builder.Property(x => x.Reach)
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer);

        builder.Property(x => x.GpsLongitude)
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Float);

        builder.Property(x => x.GpsLatitude)
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Float);

        builder.HasOne<Country>()
            .WithMany()
            .HasForeignKey(x => x.CountryCode)
            .HasPrincipalKey(c => c.CountryCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CountryCode)
            .HasDatabaseName("IX_Locations_CountryCode");
    }
}
