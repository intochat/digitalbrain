using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class OverageBillingRecordsConfiguration : IEntityTypeConfiguration<OverageBillingRecord>
{
    public void Configure(EntityTypeBuilder<OverageBillingRecord> builder)
    {
        builder.ToTable(DbConstants.Tables.OverageBillingRecords, DbConstants.SchemaName);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("OverageBillingRecordId")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.UserId)
            .IsRequired();

        builder.Property(x => x.ServiceTypeId)
            .IsRequired();

        builder.Property(x => x.OverageTokensUsed)
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Decimal18_4)
            .IsRequired();

        builder.Property(x => x.TokenUnitCost)
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Decimal18_6)
            .IsRequired();

        builder.Property(x => x.TotalCharge)
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Decimal18_2)
            .IsRequired();

        builder.Property(x => x.CurrencyId)
            .IsRequired();

        builder.Property(x => x.Year)
            .IsRequired();

        builder.Property(x => x.Month)
            .IsRequired();

        builder.Property(x => x.UsageTimestamp)
            .IsRequired();

        builder.Property(x => x.IsBilled)
            .IsRequired();

        builder.Property(x => x.BilledAt)
            .IsRequired(false);

        builder.Property(x => x.StripeInvoiceId)
            .HasMaxLength(DbConstants.Validations.MaxLengths.L255)
            .IsRequired(false);

        builder.Property(x => x.Metadata)
            .HasColumnType(DbConstants.ColumnTypes.Text.TextType)
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired(false);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ServiceType)
            .WithMany()
            .HasForeignKey(x => x.ServiceTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Currency)
            .WithMany()
            .HasForeignKey(x => x.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.UserId)
            .HasDatabaseName("IX_OverageBillingRecords_UserId");

        builder.HasIndex(x => new { x.UserId, x.Year, x.Month })
            .HasDatabaseName("IX_OverageBillingRecords_UserId_Year_Month");

        builder.HasIndex(x => x.IsBilled)
            .HasDatabaseName("IX_OverageBillingRecords_IsBilled");

        builder.HasIndex(x => x.ServiceTypeId)
            .HasDatabaseName("IX_OverageBillingRecords_ServiceTypeId");

        builder.HasIndex(x => x.UsageTimestamp)
            .HasDatabaseName("IX_OverageBillingRecords_UsageTimestamp");

        builder.HasIndex(x => x.CurrencyId)
            .HasDatabaseName("IX_OverageBillingRecords_CurrencyId");

        builder.Property(x => x.ProcessingId)
            .HasMaxLength(DbConstants.Validations.MaxLengths.L255)
            .IsRequired(false);

        builder.Property(x => x.ProcessingStartedAt)
            .IsRequired(false);

        builder.HasIndex(x => x.ProcessingId)
            .HasDatabaseName("IX_OverageBillingRecords_ProcessingId")
            .HasFilter("\"ProcessingId\" IS NOT NULL");
    }
}
