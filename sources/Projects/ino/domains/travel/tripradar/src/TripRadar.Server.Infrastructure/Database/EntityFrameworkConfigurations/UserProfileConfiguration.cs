using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable(DbConstants.Tables.UserProfiles, DbConstants.SchemaName);

        builder.HasKey(usp => usp.Id);
        builder.Property(usp => usp.Id)
            .HasColumnName("UserProfileId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(usp => usp.UserId)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.BigInt)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(usp => usp.Username)
            .IsRequired(false)
            .HasColumnType(DbConstants.ColumnTypes.Text.TextType)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(new ValueConverter<string?, string>(
                v => v.EncryptString() ?? string.Empty,
                v => v.DecryptString())
            );

        builder.Property(usp => usp.UsernameHash)
            .IsRequired(false)
            .HasMaxLength(64)
            .HasColumnType("varchar(64)")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(usp => usp.Password)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L255)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar255)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(new ValueConverter<string, string>(
                v => HashPassword(v),
                v => v)
            );

        builder.Property(usp => usp.Email)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Text.TextType)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(new ValueConverter<string, string>(
                v => v.EncryptString() ?? string.Empty,
                v => v.DecryptString())
            );

        builder.Property(usp => usp.EmailHash)
            .IsRequired(false)
            .HasMaxLength(64)
            .HasColumnType("varchar(64)")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(usp => usp.IsEmailConfirmed)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Boolean.BooleanType)
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(usp => usp.EmailConfirmationToken)
            .HasMaxLength(DbConstants.Validations.MaxLengths.L255)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar255)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(usp => usp.EmailConfirmationTokenExpiry)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                v => v.HasValue
                    ? v.Value.Kind == DateTimeKind.Utc ? v.Value : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
                    : v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v
            );

        builder.Property(usp => usp.PasswordResetToken)
            .HasMaxLength(DbConstants.Validations.MaxLengths.L255)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar255)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(usp => usp.PasswordResetTokenExpiry)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                v => v.HasValue
                    ? v.Value.Kind == DateTimeKind.Utc ? v.Value : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
                    : v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v
            );

        builder.Property(usp => usp.FirstName)
            .HasColumnType(DbConstants.ColumnTypes.Text.TextType)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(new ValueConverter<string?, string>(
                v => v.EncryptString() ?? string.Empty,
                v => v.DecryptString())
            );

        builder.Property(usp => usp.LastName)
            .HasColumnType(DbConstants.ColumnTypes.Text.TextType)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(new ValueConverter<string?, string>(
                v => v.EncryptString() ?? string.Empty,
                v => v.DecryptString())
            );

        builder.Property(usp => usp.PhoneNumber)
            .HasColumnType(DbConstants.ColumnTypes.Text.TextType)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(new ValueConverter<string?, string>(
                v => v.EncryptString() ?? string.Empty,
                v => v.DecryptString())
            );

        builder.Property(usp => usp.IpAddress)
            .HasColumnType(DbConstants.ColumnTypes.Text.TextType)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(new ValueConverter<string?, string>(
                v => v.EncryptString() ?? string.Empty,
                v => v.DecryptString())
            );

        builder.Property(usp => usp.TimezoneId)
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasDefaultValue(1);

        builder.Property(usp => usp.ProfilePictureUrl)
            .HasMaxLength(500)
            .HasColumnType("VARCHAR(500)")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(usp => usp.LanguageId)
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired(false);

        builder.Property(usp => usp.CountryId)
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired(false);

        builder.Property(usp => usp.RefreshToken)
            .IsRequired()
            .HasMaxLength(DbConstants.Validations.MaxLengths.L255)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar255)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(usp => usp.RefreshTokenExpiryTime)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
            );

        builder.Property(usp => usp.SecurityStamp)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnType("varchar(64)")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(usp => usp.GoogleId)
            .HasMaxLength(DbConstants.Validations.MaxLengths.L255)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar255)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(usp => usp.TelegramUserId)
            .HasColumnType(DbConstants.ColumnTypes.Numeric.BigInt)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired(false);

        builder.Property(usp => usp.AccessFailedCount)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Numeric.Integer)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(usp => usp.LockoutEnd)
            .IsRequired(false)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                v => v.HasValue
                    ? v.Value.Kind == DateTimeKind.Utc ? v.Value : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
                    : v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v
            );

        builder.Property(usp => usp.LockoutEnabled)
            .IsRequired()
            .HasColumnType(DbConstants.ColumnTypes.Boolean.BooleanType)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne(usp => usp.User)
            .WithOne(u => u.Profile)
            .HasForeignKey<UserProfile>(usp => usp.UserId)
            .IsRequired();

        builder.HasOne(usp => usp.Language)
            .WithMany()
            .HasForeignKey(usp => usp.LanguageId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasOne(usp => usp.Country)
            .WithMany()
            .HasForeignKey(usp => usp.CountryId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasOne(usp => usp.TimezoneReference)
            .WithMany()
            .HasForeignKey(usp => usp.TimezoneId)
            .HasPrincipalKey(t => t.TimezoneId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasIndex(usp => usp.UserId)
            .IsUnique();

        builder.HasIndex(usp => usp.UsernameHash)
            .IsUnique();

        builder.HasIndex(usp => usp.EmailHash)
            .IsUnique();

        builder.HasIndex(usp => usp.GoogleId);

        builder.HasIndex(usp => usp.TelegramUserId)
            .IsUnique();

        builder.HasIndex(usp => usp.TimezoneId);

        builder.HasIndex(usp => usp.LanguageId);

        builder.HasIndex(usp => usp.CountryId);
    }

    private static string HashPassword(string password)
    {
        return string.IsNullOrWhiteSpace(password) || password.StartsWith("$2a$")
                                                   || password.StartsWith("$2b$") || password.StartsWith("$2y$")
            ? password
            : BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt());
    }
}
