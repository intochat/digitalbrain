using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class PricesConfiguration : IEntityTypeConfiguration<Price>
{
    public void Configure(EntityTypeBuilder<Price> builder)
    {
        builder.ToTable(DbConstants.Tables.Prices, DbConstants.SchemaName);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("PriceId")
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.BigInt)
            .ValueGeneratedOnAdd();

        builder.Property(u => u.TierId)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(p => p.CurrencyId)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(p => p.BillingPeriodId)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(p => p.Amount)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.BigInt);

        builder.Property(p => p.StripeId)
            .HasMaxLength(DbConstants.Validations.MaxLengths.L255)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar255)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(new ValueConverter<string?, string>(
                v => v.EncryptString() ?? string.Empty,
                v => v.DecryptString())
            );

        builder.Property(p => p.StripeIdHash)
            .HasMaxLength(DbConstants.Validations.MaxLengths.L64)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar64)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired(false);

        builder.Property(p => p.CreatedAt)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz);

        builder.Property(p => p.UpdatedAt)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz);

        builder.HasOne(p => p.Tier)
            .WithMany()
            .HasForeignKey(p => p.TierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Currency)
            .WithMany()
            .HasForeignKey(p => p.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.BillingPeriod)
            .WithMany()
            .HasForeignKey(p => p.BillingPeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.Id).IsUnique();
        builder.HasIndex(p => p.TierId);
        builder.HasIndex(p => p.CurrencyId);
        builder.HasIndex(p => p.BillingPeriodId);
        builder.HasIndex(p => p.CreatedAt);
        builder.HasIndex(p => p.StripeIdHash);
    }
}
