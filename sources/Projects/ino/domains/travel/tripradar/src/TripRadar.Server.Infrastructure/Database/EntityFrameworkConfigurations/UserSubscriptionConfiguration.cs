using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.ToTable(DbConstants.Tables.UserSubscriptions, DbConstants.SchemaName);

        builder.HasKey(us => us.Id);

        builder.Property(us => us.Id)
            .HasColumnName("UserSubscriptionId")
            .IsRequired()
            .ValueGeneratedOnAdd();

        builder.Property(us => us.UserId)
            .HasColumnName("UserId")
            .IsRequired();

        builder.Property(us => us.StripeCustomerId)
            .HasColumnName("StripeCustomerId")
            .HasMaxLength(DbConstants.Validations.MaxLengths.L255)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar255)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(us => us.StripeSubscriptionId)
            .HasColumnName("StripeSubscriptionId")
            .HasMaxLength(DbConstants.Validations.MaxLengths.L255)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar255)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(us => us.SubscriptionExpirationTime)
            .HasColumnName("SubscriptionExpirationTime")
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(us => us.PendingTierId)
            .HasColumnName("PendingTierId")
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(us => us.IsActive)
            .HasColumnName("IsActive")
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Boolean.BooleanType)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(us => us.PayAsYouGoEnabled)
            .HasColumnName("PayAsYouGoEnabled")
            .HasColumnType(DbConstants.ColumnTypes.Boolean.BooleanType)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(us => us.CreatedAt)
            .HasColumnName("CreatedAt")
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(us => us.UpdatedAt)
            .HasColumnName("UpdatedAt")
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(us => us.DeferredDowngradeJobId)
            .HasColumnName("DeferredDowngradeJobId")
            .HasMaxLength(DbConstants.Validations.MaxLengths.L255)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar255)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.HasOne(us => us.User)
            .WithOne(u => u.UserSubscription)
            .HasForeignKey<UserSubscription>(us => us.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(us => us.UserId)
            .HasDatabaseName("IX_UserSubscriptions_UserId")
            .IsUnique();

        builder.HasIndex(us => us.StripeSubscriptionId)
            .HasDatabaseName("IX_UserSubscriptions_StripeSubscriptionId");

        builder.HasIndex(us => us.StripeCustomerId)
            .HasDatabaseName("IX_UserSubscriptions_StripeCustomerId");

        builder.HasOne(us => us.PendingTier)
            .WithMany()
            .HasForeignKey(us => us.PendingTierId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(us => us.PendingTierId)
            .HasDatabaseName("IX_UserSubscriptions_PendingTierId");
    }
}
