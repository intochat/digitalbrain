using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class TiersConfiguration : IEntityTypeConfiguration<Tier>
{
    public void Configure(EntityTypeBuilder<Tier> builder)
    {
        builder.ToTable(DbConstants.Tables.Tiers, DbConstants.SchemaName);

        builder.Property(t => t.Id)
            .HasColumnName("TierId")
            .IsRequired()
            .ValueGeneratedOnAdd();

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L50)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar50)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(t => t.TokensPerMonthLimit)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Decimal10_2)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany<User>("Users")
            .WithOne(u => u.Tier)
            .HasForeignKey(u => u.TierId);

        builder.HasIndex(t => t.Name)
            .IsUnique()
            .HasDatabaseName("IX_Tiers_Name");
    }
}
