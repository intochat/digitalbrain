using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class UsersConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable(DbConstants.Tables.Users, DbConstants.SchemaName);

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id)
            .HasColumnName("UserId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(u => u.CreatedOn)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
            );

        builder.Property(u => u.UpdatedOn)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                v => v.HasValue
                    ? v.Value.Kind == DateTimeKind.Utc ? v.Value : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
                    : v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v
            );

        builder.Property(u => u.IsActive)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Boolean.BooleanType)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(u => u.TierId)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(u => u.HasDataStorageConsent)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Boolean.BooleanType)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(u => u.AllowsMarketingEmails)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Boolean.BooleanType)
            .HasDefaultValue(false)
            .ValueGeneratedNever()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(u => u.PromoCodeId)
            .HasColumnType(DbConstants.ColumnTypes.Numeric.BigInt)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne(u => u.PromoCode)
            .WithMany("Users")
            .HasForeignKey(u => u.PromoCodeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(u => u.PromoCodeId)
            .HasDatabaseName("IX_Users_PromoCodeId");

        builder.HasOne(u => u.Tier)
            .WithMany("Users")
            .HasForeignKey(u => u.TierId)
            .IsRequired();

        builder.HasIndex(u => u.TierId)
            .HasDatabaseName("IX_Users_TierId");

        builder.HasMany<UserMonthlyTokenCount>("MonthlyTokenCounts")
            .WithOne("User")
            .HasForeignKey(umtc => umtc.UserId);

        builder.HasMany<ScheduledFlightQuery>("ScheduledFlightQueries")
            .WithOne(sfq => sfq.User)
            .HasForeignKey(sfq => sfq.UserId);

        builder.HasMany<ScheduledHotelQuery>("ScheduledHotelQueries")
            .WithOne(sfq => sfq.User)
            .HasForeignKey(sfq => sfq.UserId);

        builder.HasMany<ScheduledEventQuery>("ScheduledEventQueries")
            .WithOne(sfq => sfq.User)
            .HasForeignKey(sfq => sfq.UserId);

        builder.HasMany<ScheduledLocalPlaceQuery>("ScheduledLocalPlacesQueries")
            .WithOne(f => f.User)
            .HasForeignKey(f => f.UserId);

        builder.HasOne(u => u.Profile)
            .WithOne(usp => usp.User)
            .HasForeignKey<UserProfile>(usp => usp.UserId)
            .IsRequired();

        builder.HasMany<Feedback>("Feedbacks")
            .WithOne(f => f.User)
            .HasForeignKey(f => f.UserId);

        builder.HasMany<TripVault>("TripVaults")
            .WithOne(tv => tv.Owner)
            .HasForeignKey(tv => tv.OwnerId);
    }
}
