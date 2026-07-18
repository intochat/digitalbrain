using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class PreferenceTypeConfiguration : IEntityTypeConfiguration<PreferenceType>
{
    public void Configure(EntityTypeBuilder<PreferenceType> builder)
    {
        builder.ToTable(DbConstants.Tables.PreferenceTypes, DbConstants.SchemaName);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("PreferenceTypeId");

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100);

        builder.Property(e => e.DataType)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L50)
            .HasConversion(
                v => v.Name,
                v => Enumeration.GetAll<Domain.Enums.PreferenceDataType>().FirstOrDefault(x => x.Name == v) ?? Domain.Enums.PreferenceDataType.String);

        builder.Property(e => e.ValidationSchema)
            .HasColumnType(DbConstants.ColumnTypes.Json.Jsonb);

        builder.Property(e => e.IsRequired)
            .HasColumnType(DbConstants.ColumnTypes.Boolean.BooleanType)
            .HasDefaultValue(false);

        builder.Property(e => e.DefaultValue)
            .HasMaxLength(DbConstants.Validations.MaxLengths.L500);

        builder.Property(e => e.IsActive)
            .HasColumnType(DbConstants.ColumnTypes.Boolean.BooleanType)
            .HasDefaultValue(true);

        builder.Property(e => e.CreatedAt)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .HasDefaultValueSql(DbConstants.ColumnTypes.DefaultValueSql.Now);

        builder.Property(e => e.UpdatedAt)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .HasDefaultValueSql(DbConstants.ColumnTypes.DefaultValueSql.Now);

        builder.HasOne(e => e.ServiceType)
            .WithMany()
            .HasForeignKey(e => e.ServiceTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.ServiceTypeId);
        builder.HasIndex(e => new { e.ServiceTypeId, e.Name }).IsUnique();
        builder.HasIndex(e => e.IsActive);
    }
}
