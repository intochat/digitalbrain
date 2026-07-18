using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class AirlinesConfiguration : IEntityTypeConfiguration<Airline>
{
    public void Configure(EntityTypeBuilder<Airline> builder)
    {
        builder.ToTable(DbConstants.Tables.Airlines, DbConstants.SchemaName);

        builder.Property<int>("AirlineId")
            .HasColumnName("AirlineId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasKey("AirlineId");

        builder.Property(a => a.AirlineCode)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L32);

        builder.Property(a => a.AirlineName)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L255);

        builder.Property(a => a.SearchAliases)
            .HasColumnType(DbConstants.ColumnTypes.Text.TextType);

        builder.Property(a => a.IsAlliance)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Boolean.BooleanType)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(a => a.IsActive)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Boolean.BooleanType)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(a => a.AirlineCode)
            .IsUnique()
            .HasDatabaseName("IX_Airlines_AirlineCode");

        builder.HasIndex(a => a.AirlineName)
            .HasDatabaseName("IX_Airlines_AirlineName");
    }
}
