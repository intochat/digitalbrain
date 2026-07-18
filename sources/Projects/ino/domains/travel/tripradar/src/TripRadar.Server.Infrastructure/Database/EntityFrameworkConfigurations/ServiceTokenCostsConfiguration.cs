using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class ServiceTokenCostsConfiguration : IEntityTypeConfiguration<ServiceTokenCost>
{
    public void Configure(EntityTypeBuilder<ServiceTokenCost> builder)
    {
        builder.ToTable(DbConstants.Tables.ServiceTokenCosts, DbConstants.SchemaName);

        builder.Property(st => st.Id)
            .HasColumnName("ServiceTokenCostId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasKey(st => st.Id);

        builder.Property(stc => stc.Cost)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Decimal10_2);

        builder.Property(stc => stc.ServiceTypeId)
            .IsRequired();

        builder.HasOne<ServiceType>()
            .WithMany()
            .HasForeignKey(stc => stc.ServiceTypeId)
            .HasConstraintName("FK_ServiceTokenCosts_ServiceTypes_ServiceTypeId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
