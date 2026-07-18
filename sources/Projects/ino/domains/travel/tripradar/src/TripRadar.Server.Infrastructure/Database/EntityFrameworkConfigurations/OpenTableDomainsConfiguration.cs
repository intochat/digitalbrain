using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class OpenTableDomainsConfiguration : IEntityTypeConfiguration<OpenTableDomain>
{
    public void Configure(EntityTypeBuilder<OpenTableDomain> builder)
    {
        builder.ToTable(DbConstants.Tables.OpenTableDomains, DbConstants.SchemaName);

        builder.Property<int>("OpenTableDomainId")
            .HasColumnName("OpenTableDomainId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasKey("OpenTableDomainId");

        builder.Property(d => d.DomainName)
            .HasColumnName("Domain")
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(d => d.Country)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(d => d.DomainName)
            .IsUnique();
    }
}

