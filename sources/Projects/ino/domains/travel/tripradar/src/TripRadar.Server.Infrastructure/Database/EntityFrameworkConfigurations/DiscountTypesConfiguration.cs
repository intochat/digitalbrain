using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class DiscountTypesConfiguration : IEntityTypeConfiguration<DiscountType>
{
    public void Configure(EntityTypeBuilder<DiscountType> builder)
    {
        builder.ToTable(DbConstants.Tables.DiscountTypes, DbConstants.SchemaName);

        builder.Property(dt => dt.Id)
            .HasColumnName("DiscountTypeId")
            .IsRequired()
            .ValueGeneratedNever();

        builder.HasKey(dt => dt.Id);

        builder.Property(dt => dt.Name)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L50)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar50)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(dt => dt.Description)
            .HasMaxLength(DbConstants.Validations.MaxLengths.L200)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar200)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(dt => dt.CreatedAt)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(dt => dt.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany<PromoCode>("PromoCodes")
            .WithOne(pc => pc.DiscountType)
            .HasForeignKey(pc => pc.DiscountTypeId);

        builder.HasIndex(dt => dt.Name)
            .IsUnique()
            .HasDatabaseName("IX_DiscountTypes_Name");
    }
}
