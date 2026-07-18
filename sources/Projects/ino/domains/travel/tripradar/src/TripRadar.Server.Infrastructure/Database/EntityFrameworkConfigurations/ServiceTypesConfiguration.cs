using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using ServiceType = TripRadar.Server.Domain.ReferenceData.ServiceType;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class ServiceTypesConfiguration : IEntityTypeConfiguration<ServiceType>
{
    public void Configure(EntityTypeBuilder<ServiceType> builder)
    {
        builder.ToTable(DbConstants.Tables.ServiceTypes, DbConstants.SchemaName);

        builder.Property(st => st.Id)
            .HasColumnName("ServiceTypeId")
            .IsRequired()
            .ValueGeneratedOnAdd();

        builder.HasKey(st => st.Id);

        builder.Property(st => st.Name)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar100);

        builder.Property(st => st.PreferenceCategoryId)
            .IsRequired(false);

        builder.HasOne(st => st.PreferenceCategory)
            .WithMany()
            .HasForeignKey(st => st.PreferenceCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(st => st.Name)
            .IsUnique()
            .HasDatabaseName("IX_ServiceTypes_Name");

        builder.HasIndex(st => st.PreferenceCategoryId)
            .HasDatabaseName("IX_ServiceTypes_PreferenceCategoryId");
    }
}
