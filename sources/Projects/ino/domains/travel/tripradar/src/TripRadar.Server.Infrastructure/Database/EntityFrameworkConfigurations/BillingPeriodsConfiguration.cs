using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class BillingPeriodsConfiguration : IEntityTypeConfiguration<BillingPeriod>
{
    public void Configure(EntityTypeBuilder<BillingPeriod> builder)
    {
        builder.ToTable(DbConstants.Tables.BillingPeriods, DbConstants.SchemaName);

        builder.Property<int>("BillingPeriodId")
            .HasColumnName("BillingPeriodId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasKey("BillingPeriodId");

        builder.Property(bp => bp.Name)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L50)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar50)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(bp => bp.Name)
            .IsUnique()
            .HasDatabaseName("IX_BillingPeriods_Name");
    }
}
