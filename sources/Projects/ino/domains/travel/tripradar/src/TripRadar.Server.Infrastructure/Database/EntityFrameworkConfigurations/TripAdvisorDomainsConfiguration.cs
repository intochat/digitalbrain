using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class TripAdvisorDomainsConfiguration : IEntityTypeConfiguration<TripAdvisorDomain>
{
    public void Configure(EntityTypeBuilder<TripAdvisorDomain> builder)
    {
        builder.ToTable(DbConstants.Tables.TripAdvisorDomains, DbConstants.SchemaName);

        builder.Property<int>("TripAdvisorDomainId")
            .HasColumnName("TripAdvisorDomainId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasKey("TripAdvisorDomainId");

        builder.Property(d => d.DomainName)
            .HasColumnName("Domain")
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(d => d.Title)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(d => d.Locale)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L50)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(d => d.DomainName)
            .IsUnique();
    }
}
