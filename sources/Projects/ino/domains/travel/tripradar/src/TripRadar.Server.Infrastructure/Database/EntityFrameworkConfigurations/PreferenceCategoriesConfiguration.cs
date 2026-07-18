using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using PreferenceCategory = TripRadar.Server.Domain.ReferenceData.PreferenceCategory;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class PreferenceCategoriesConfiguration : IEntityTypeConfiguration<PreferenceCategory>
{
    public void Configure(EntityTypeBuilder<PreferenceCategory> builder)
    {
        builder.ToTable(DbConstants.Tables.PreferenceCategories, DbConstants.SchemaName);

        builder.Property(pc => pc.Id)
            .HasColumnName("PreferenceCategoryId")
            .IsRequired()
            .ValueGeneratedOnAdd();

        builder.HasKey(pc => pc.Id);

        builder.Property(pc => pc.Name)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar100);

        builder.Property(pc => pc.IsActive)
            .HasColumnType(DbConstants.ColumnTypes.Boolean.BooleanType)
            .HasDefaultValue(true);

        builder.HasIndex(pc => pc.Name)
            .IsUnique()
            .HasDatabaseName("IX_PreferenceCategories_Name");
    }
}
