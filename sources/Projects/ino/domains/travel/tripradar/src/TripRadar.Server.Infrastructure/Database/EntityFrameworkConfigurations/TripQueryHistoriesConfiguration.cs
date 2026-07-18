using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class TripQueryHistoriesConfiguration : IEntityTypeConfiguration<TripQueryHistory>
{
    public void Configure(EntityTypeBuilder<TripQueryHistory> builder)
    {
        builder.ToTable(DbConstants.Tables.TripQueryHistories, DbConstants.SchemaName);

        builder.HasKey(tqh => tqh.Id);
        builder.Property(tqh => tqh.Id)
            .HasColumnName("TripQueryHistoryId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(tqh => tqh.UniqueId)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Identifier.Uuid)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(tqh => tqh.TripVaultId)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.BigInt)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(tqh => tqh.ServiceTypeId)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(tqh => tqh.QueryParametersJson)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Json.Jsonb)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(tqh => tqh.StartDateTime)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                v => v.HasValue
                    ? v.Value.Kind == DateTimeKind.Utc ? v.Value : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
                    : v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v
            );

        builder.Property(tqh => tqh.EndDateTime)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                v => v.HasValue
                    ? v.Value.Kind == DateTimeKind.Utc ? v.Value : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
                    : v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v
            );

        builder.Property(tqh => tqh.ResultSummary)
            .HasColumnType(DbConstants.ColumnTypes.Text.TextType)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(tqh => tqh.CreatedOn)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .HasDefaultValueSql(DbConstants.ColumnTypes.DefaultValueSql.Now)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
            );

        // Note: The relationship with TripVault is configured in TripVaultsConfiguration
        // to avoid duplicate configuration that causes shadow property issues

        builder.HasIndex(tqh => tqh.UniqueId).IsUnique();
        builder.HasIndex(tqh => tqh.TripVaultId);
        builder.HasIndex(tqh => tqh.ServiceTypeId);
        builder.HasIndex(tqh => new { tqh.TripVaultId, tqh.CreatedOn })
            .IsDescending(false, true);
    }
}
