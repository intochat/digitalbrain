using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class YelpDomainsConfiguration : IEntityTypeConfiguration<YelpDomain>
{
    public void Configure(EntityTypeBuilder<YelpDomain> builder)
    {
        builder.ToTable(DbConstants.Tables.YelpDomains, DbConstants.SchemaName);

        builder.Property<int>("YelpDomainId")
            .HasColumnName("YelpDomainId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasKey("YelpDomainId");

        builder.Property(d => d.DomainName)
            .HasColumnName("Domain")
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(d => d.Locale)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(d => d.DomainName)
            .IsUnique();
    }
}

