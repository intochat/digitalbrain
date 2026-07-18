using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class OveragePricingsConfiguration : IEntityTypeConfiguration<OveragePricing>
{
    public void Configure(EntityTypeBuilder<OveragePricing> builder)
    {
        builder.ToTable(DbConstants.Tables.OveragePricing, DbConstants.SchemaName);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("OveragePricingId")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.TierId)
            .IsRequired();

        builder.Property(x => x.PricePerToken)
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Decimal18_6)
            .IsRequired();

        builder.Property(p => p.CurrencyId)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => new { x.TierId, x.IsActive })
            .HasDatabaseName("IX_OveragePricing_TierId_IsActive");

        builder.HasIndex(x => x.IsActive)
            .HasDatabaseName("IX_OveragePricing_IsActive");

        builder.HasOne(p => p.Currency)
            .WithMany()
            .HasForeignKey(p => p.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Tier>()
            .WithMany()
            .HasForeignKey(p => p.TierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
