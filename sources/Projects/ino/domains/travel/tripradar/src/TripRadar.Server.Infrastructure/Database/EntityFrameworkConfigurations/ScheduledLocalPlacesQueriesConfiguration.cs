using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Comms.Core.Convertors;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.ValueObjects;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class ScheduledLocalPlaceQueryConfiguration : IEntityTypeConfiguration<ScheduledLocalPlaceQuery>
{
    public void Configure(EntityTypeBuilder<ScheduledLocalPlaceQuery> builder)
    {
        builder.ToTable(DbConstants.Tables.ScheduledLocalPlacesQueries, DbConstants.SchemaName);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("ScheduledLocalPlacesQueryId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(e => e.UserId)
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(e => e.SearchQuery)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar500)
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(e => e.ScheduledExecutionId)
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(e => e.CreatedOn)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .HasDefaultValueSql(DbConstants.ColumnTypes.DefaultValueSql.Now)
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(e => e.UpdatedOn)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .IsRequired(false)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(e => e.UniqueId)
            .HasColumnType(DbConstants.ColumnTypes.Identifier.Uuid)
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(e => e.AdditionalParameters)
            .HasColumnType(DbConstants.ColumnTypes.Json.Jsonb)
            .IsRequired(false)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(e => e.SelectedColumns)
            .HasColumnType(DbConstants.ColumnTypes.Json.Jsonb)
            .IsRequired(false)
            .HasConversion<StringValueConverter<IList<QueryColumn>, List<QueryColumn>>>()
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne(e => e.User)
            .WithMany("ScheduledLocalPlacesQueries")
            .HasForeignKey(e => e.UserId)
            .HasPrincipalKey(a => a.Id);

        builder.HasOne(e => e.ScheduledExecution)
            .WithMany()
            .HasForeignKey(e => e.ScheduledExecutionId)
            .HasPrincipalKey(s => s.Id)
            .IsRequired();

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.ScheduledExecutionId);
        builder.HasIndex(e => e.UniqueId);
    }
}
