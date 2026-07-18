using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class TimezonesConfiguration : IEntityTypeConfiguration<Timezone>
{
    public void Configure(EntityTypeBuilder<Timezone> builder)
    {
        builder.ToTable(DbConstants.Tables.Timezones, DbConstants.SchemaName);

        builder.HasKey(t => t.TimezoneId);

        builder.Property(t => t.TimezoneId)
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(t => t.TimezoneCode)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar100)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(t => t.TimezoneName)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar100)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(t => t.TimezoneCode)
            .IsUnique()
            .HasDatabaseName("IX_Timezones_TimezoneCode");

        builder.HasIndex(t => t.TimezoneName)
            .HasDatabaseName("IX_Timezones_TimezoneName");
    }
}
