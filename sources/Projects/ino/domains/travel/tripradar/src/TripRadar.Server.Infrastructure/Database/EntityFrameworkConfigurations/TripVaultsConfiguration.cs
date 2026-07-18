using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class TripVaultsConfiguration : IEntityTypeConfiguration<TripVault>
{
    public void Configure(EntityTypeBuilder<TripVault> builder)
    {
        builder.ToTable(DbConstants.Tables.TripVaults, DbConstants.SchemaName);

        builder.HasKey(tv => tv.Id);
        builder.Property(tv => tv.Id)
            .HasColumnName("TripVaultId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(tv => tv.UniqueId)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Identifier.Uuid)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(tv => tv.OwnerId)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.BigInt)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(tv => tv.Name)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L255)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar255)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(tv => tv.Description)
            .HasMaxLength(DbConstants.Validations.MaxLengths.L500)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar500)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(tv => tv.StartDate)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                v => v.HasValue
                    ? v.Value.Kind == DateTimeKind.Utc ? v.Value : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
                    : v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v
            );

        builder.Property(tv => tv.EndDate)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                v => v.HasValue
                    ? v.Value.Kind == DateTimeKind.Utc ? v.Value : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
                    : v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v
            );

        builder.Property(tv => tv.CreatedOn)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .HasDefaultValueSql(DbConstants.ColumnTypes.DefaultValueSql.Now)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
            );

        builder.Property(tv => tv.UpdatedOn)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                v => v.HasValue
                    ? v.Value.Kind == DateTimeKind.Utc ? v.Value : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
                    : v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v
            );

        builder.HasOne(tv => tv.Owner)
            .WithMany("TripVaults")
            .HasForeignKey(tv => tv.OwnerId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // Ignore the computed QueryHistory property - it's not a navigation property
        builder.Ignore(tv => tv.QueryHistory);

        // Configure the actual navigation property for the one-to-many relationship
        builder.HasMany<TripQueryHistory>("QueryHistoryInternal")
            .WithOne(tqh => tqh.TripVault)
            .HasForeignKey(tqh => tqh.TripVaultId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(tv => tv.UniqueId).IsUnique();
        builder.HasIndex(tv => tv.OwnerId);
        builder.HasIndex(tv => new { tv.OwnerId, tv.CreatedOn })
            .IsDescending(false, true);
    }
}
