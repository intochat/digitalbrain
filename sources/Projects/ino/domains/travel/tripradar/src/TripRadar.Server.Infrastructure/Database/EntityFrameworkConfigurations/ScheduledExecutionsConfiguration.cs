using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class ScheduledExecutionConfiguration : IEntityTypeConfiguration<ScheduledExecution>
{
    public void Configure(EntityTypeBuilder<ScheduledExecution> builder)
    {
        builder.ToTable(DbConstants.Tables.ScheduledExecutions, DbConstants.SchemaName);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("ScheduledExecutionId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(e => e.UniqueId)
            .HasColumnType(DbConstants.ColumnTypes.Identifier.Uuid)
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(e => e.UserId)
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(e => e.Name)
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(u => u.IsActive)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Boolean.BooleanType)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(e => e.NextExecutionTime)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(e => e.Schedule)
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(e => e.CreatedOn)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .HasDefaultValueSql(DbConstants.ColumnTypes.DefaultValueSql.Now)
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(e => e.UpdatedOn)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .IsRequired(false)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("IX_ScheduledExecutions_UserId");

        builder.HasIndex(e => e.IsActive)
            .HasDatabaseName("IX_ScheduledExecutions_IsActive");

        builder.HasIndex(e => e.NextExecutionTime)
            .HasDatabaseName("IX_ScheduledExecutions_NextExecutionTime");

        builder.HasIndex(e => new { e.IsActive, e.NextExecutionTime })
            .HasDatabaseName("IX_ScheduledExecutions_IsActive_NextExecutionTime");

        builder.Property(e => e.TripVaultId)
            .IsRequired(false)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.HasOne(e => e.TripVault)
            .WithMany()
            .HasForeignKey(e => e.TripVaultId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.TripVaultId)
            .HasDatabaseName("IX_ScheduledExecutions_TripVaultId");
    }
}
