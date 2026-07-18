using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class UsageEventsConfiguration : IEntityTypeConfiguration<UsageEvent>
{
    public void Configure(EntityTypeBuilder<UsageEvent> builder)
    {
        builder.ToTable(DbConstants.Tables.UsageEvents, DbConstants.SchemaName);

        builder.HasKey(eventItem => eventItem.Id);

        builder.Property(eventItem => eventItem.Id)
            .HasColumnName("UsageEventId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(eventItem => eventItem.UniqueId)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Identifier.Uuid)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(eventItem => eventItem.UserId)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.BigInt)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(eventItem => eventItem.ServiceTypeId)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(eventItem => eventItem.TripVaultId)
            .HasColumnType(DbConstants.ColumnTypes.Numeric.BigInt)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(eventItem => eventItem.UsageEventSourceId)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(eventItem => eventItem.TokensConsumed)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Decimal10_2)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(eventItem => eventItem.OccurredAt)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                value => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc),
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        builder.Property(eventItem => eventItem.CreatedAt)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                value => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc),
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        builder.HasOne(eventItem => eventItem.User)
            .WithMany()
            .HasForeignKey(eventItem => eventItem.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(eventItem => eventItem.ServiceType)
            .WithMany()
            .HasForeignKey(eventItem => eventItem.ServiceTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(eventItem => eventItem.TripVault)
            .WithMany()
            .HasForeignKey(eventItem => eventItem.TripVaultId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(eventItem => eventItem.UsageEventSource)
            .WithMany()
            .HasForeignKey(eventItem => eventItem.UsageEventSourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(eventItem => eventItem.UniqueId)
            .IsUnique();

        builder.HasIndex(eventItem => new { eventItem.UserId, eventItem.OccurredAt })
            .IsDescending(false, true);

        builder.HasIndex(eventItem => new { eventItem.UserId, eventItem.ServiceTypeId, eventItem.OccurredAt })
            .IsDescending(false, false, true);

        builder.HasIndex(eventItem => new { eventItem.UserId, eventItem.TripVaultId, eventItem.OccurredAt })
            .IsDescending(false, false, true);

        builder.HasIndex(eventItem => new { eventItem.UserId, eventItem.UsageEventSourceId, eventItem.OccurredAt })
            .IsDescending(false, false, true);
    }
}

