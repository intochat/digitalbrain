using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class PromoCodeUsagesConfiguration : IEntityTypeConfiguration<PromoCodeUsage>
{
    public void Configure(EntityTypeBuilder<PromoCodeUsage> builder)
    {
        builder.ToTable(DbConstants.Tables.PromoCodeUsages, DbConstants.SchemaName);

        builder.Property(pcu => pcu.Id)
            .HasColumnName("PromoCodeUsageId")
            .IsRequired()
            .ValueGeneratedOnAdd();

        builder.HasKey(pcu => pcu.Id);

        builder.Property(pcu => pcu.PromoCodeId)
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(pcu => pcu.UserId)
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(pcu => pcu.UsedAt)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                v => v.ToUniversalTime(),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        builder.Property(pcu => pcu.DiscountApplied)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Decimal18_2)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne(pcu => pcu.PromoCode)
            .WithMany()
            .HasForeignKey(pcu => pcu.PromoCodeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(pcu => pcu.User)
            .WithMany()
            .HasForeignKey(pcu => pcu.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(pcu => pcu.PromoCodeId)
            .HasDatabaseName("IX_PromoCodeUsages_PromoCodeId");

        builder.HasIndex(pcu => pcu.UserId)
            .HasDatabaseName("IX_PromoCodeUsages_UserId");

        builder.HasIndex(pcu => new { pcu.PromoCodeId, pcu.UserId })
            .HasDatabaseName("IX_PromoCodeUsages_PromoCodeId_UserId");
    }
}
