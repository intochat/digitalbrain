using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class UserPreferencesConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> builder)
    {
        builder.ToTable(DbConstants.Tables.UserPreferences, DbConstants.SchemaName);

        builder.Property(t => t.Id)
            .HasColumnName("UserPreferenceId")
            .IsRequired()
            .ValueGeneratedOnAdd();

        builder.HasKey(e => e.Id);

        builder.Property(e => e.UserId)
            .IsRequired();

        builder.Property(e => e.PreferenceTypeId)
            .IsRequired()
            .HasColumnName("PreferenceTypeId");

        builder.Property(e => e.PreferencesJson)
            .IsRequired()
            .HasColumnName("Value")
            .HasColumnType(DbConstants.ColumnTypes.Json.Jsonb);

        builder.Property(e => e.CreatedAt)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .HasDefaultValueSql(DbConstants.ColumnTypes.DefaultValueSql.Now);

        builder.Property(e => e.UpdatedAt)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .HasDefaultValueSql(DbConstants.ColumnTypes.DefaultValueSql.Now);

        builder.Property(us => us.IsActive)
            .HasColumnName("IsActive")
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Boolean.BooleanType)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.PreferenceType)
            .WithMany()
            .HasForeignKey(e => e.PreferenceTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.UserId, e.PreferenceTypeId }).IsUnique();
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.PreferenceTypeId);
        builder.HasIndex(e => e.IsActive);
    }
}
