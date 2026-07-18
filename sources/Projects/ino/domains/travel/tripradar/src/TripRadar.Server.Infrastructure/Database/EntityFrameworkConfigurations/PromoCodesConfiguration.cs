using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class PromoCodesConfiguration : IEntityTypeConfiguration<PromoCode>
{
    public void Configure(EntityTypeBuilder<PromoCode> builder)
    {
        builder.ToTable(DbConstants.Tables.PromoCodes, DbConstants.SchemaName);

        builder.Property(pc => pc.Id)
            .HasColumnName("PromoCodeId")
            .IsRequired()
            .ValueGeneratedOnAdd();

        builder.HasKey(pc => pc.Id);

        builder.Property(pc => pc.Code)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L50)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar50);

        builder.Property(pc => pc.Description)
            .HasMaxLength(DbConstants.Validations.MaxLengths.L500)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar500);

        builder.Property(pc => pc.DiscountTypeId)
            .IsRequired();

        builder.Property(pc => pc.DiscountValue)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Decimal18_2);

        builder.Property(pc => pc.MaxUsageCount)
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer);

        builder.Property(pc => pc.CurrentUsageCount)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer);

        builder.Property(pc => pc.MaxUsagePerUser)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer);

        builder.Property(pc => pc.StartDate)
            .HasColumnName("StartDate")
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .HasConversion(
                v => v.ToUniversalTime(),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        builder.Property(pc => pc.EndDate)
            .HasColumnName("EndDate")
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .HasConversion(
                v => v.ToUniversalTime(),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        builder.Property(pc => pc.IsActive)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Boolean.BooleanType)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(pc => pc.IsDeleted)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Boolean.BooleanType)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(pc => pc.CreatedAt)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .HasConversion(
                v => v.ToUniversalTime(),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        builder.Property(pc => pc.UpdatedAt)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .HasConversion(
                v => v.HasValue ? v.Value.ToUniversalTime() : (DateTime?)null,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : (DateTime?)null);

        builder.HasOne(pc => pc.DiscountType)
            .WithMany()
            .HasForeignKey(pc => pc.DiscountTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany<PromoCodeUsage>("PromoCodeUsages")
            .WithOne(pcu => pcu.PromoCode)
            .HasForeignKey(pcu => pcu.PromoCodeId);

        builder.HasMany<User>("Users")
            .WithOne(u => u.PromoCode)
            .HasForeignKey(u => u.PromoCodeId);

        builder.HasIndex(pc => pc.Code)
            .IsUnique()
            .HasDatabaseName("IX_PromoCodes_Code");

        builder.HasIndex(pc => pc.DiscountTypeId)
            .HasDatabaseName("IX_PromoCodes_DiscountTypeId");

        builder.HasIndex(pc => new { pc.IsActive, pc.EndDate })
            .HasDatabaseName("IX_PromoCodes_IsActive_EndDate");
    }
}
