using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class UsageEventSourcesConfiguration : IEntityTypeConfiguration<UsageEventSource>
{
    public void Configure(EntityTypeBuilder<UsageEventSource> builder)
    {
        builder.ToTable(DbConstants.Tables.UsageEventSources, DbConstants.SchemaName);

        builder.HasKey(source => source.Id);

        builder.Property(source => source.Id)
            .HasColumnName("UsageEventSourceId")
            .ValueGeneratedNever()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(source => source.Name)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L50)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar50)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(source => source.Description)
            .HasMaxLength(DbConstants.Validations.MaxLengths.L200)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar200)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(source => source.IsActive)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Boolean.BooleanType)
            .HasDefaultValue(true)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(source => source.Name)
            .IsUnique()
            .HasDatabaseName("IX_UsageEventSources_Name");
    }
}
