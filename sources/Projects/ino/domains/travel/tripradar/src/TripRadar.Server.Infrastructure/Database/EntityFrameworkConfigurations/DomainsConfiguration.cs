using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class DomainsConfiguration : IEntityTypeConfiguration<GoogleDomain>
{
    public void Configure(EntityTypeBuilder<GoogleDomain> builder)
    {
        builder.ToTable(DbConstants.Tables.Domains, DbConstants.SchemaName);

        builder.Property<int>("DomainId")
            .HasColumnName("DomainId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasKey("DomainId");

        builder.Property(d => d.DomainName)
            .HasColumnName("Domain")
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(d => d.LanguageCode)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L10)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(d => d.CountryCode)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L2)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(d => d.CountryName)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L100)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(d => d.DomainName)
            .IsUnique();

        builder.HasOne<Country>()
            .WithMany()
            .HasForeignKey(d => d.CountryCode)
            .HasPrincipalKey(c => c.CountryCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Language>()
            .WithMany()
            .HasForeignKey(d => d.LanguageCode)
            .HasPrincipalKey(l => l.LanguageCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.CountryCode)
            .HasDatabaseName("IX_Domains_CountryCode");

        builder.HasIndex(d => d.LanguageCode)
            .HasDatabaseName("IX_Domains_LanguageCode");
    }
}

