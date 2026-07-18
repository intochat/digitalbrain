using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class UserMonthlyTokenCountsConfiguration : IEntityTypeConfiguration<UserMonthlyTokenCount>
{
    public void Configure(EntityTypeBuilder<UserMonthlyTokenCount> builder)
    {
        builder.ToTable(DbConstants.Tables.UserMonthlyTokenCounts, DbConstants.SchemaName);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("UserMonthlyTokenCountId")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.BigInt);

        builder.Property(x => x.TokensConsumed)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Decimal10_2);

        builder.Property(x => x.OverageTokensConsumed)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Decimal10_2);

        builder.Property(x => x.Year)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer);

        builder.Property(x => x.Month)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer);

        builder.Property(x => x.TimeZone)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar50);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz);

        builder.Property(x => x.LastUpdateTime)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.UserId)
            .HasDatabaseName("IX_UserMonthlyTokenCounts_UserId");

        builder.HasIndex(x => new { x.UserId, x.Year, x.Month })
            .HasDatabaseName("IX_UserMonthlyTokenCounts_UserId_Year_Month")
            .IsUnique();

        // Covering index for queries filtering by Year and Month (e.g. ResetTokensJob)
        builder.HasIndex(x => new { x.Year, x.Month, x.UserId })
            .HasDatabaseName("IX_UserMonthlyTokenCounts_Year_Month_UserId");
    }
}
