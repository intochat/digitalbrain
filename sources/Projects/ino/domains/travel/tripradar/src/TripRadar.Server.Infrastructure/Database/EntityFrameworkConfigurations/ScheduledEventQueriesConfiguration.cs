using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TripRadar.Server.Comms.Core.Convertors;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.ValueObjects;

namespace TripRadar.Server.Infrastructure.Database.EntityFrameworkConfigurations;

public class ScheduledEventQueriesConfiguration : IEntityTypeConfiguration<ScheduledEventQuery>
{
    public void Configure(EntityTypeBuilder<ScheduledEventQuery> builder)
    {
        builder.ToTable(DbConstants.Tables.ScheduledEventQueries, DbConstants.SchemaName);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("ScheduledEventQueryId")
            .IsRequired()
            .ValueGeneratedOnAdd()
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(e => e.UniqueId)
            .HasColumnType(DbConstants.ColumnTypes.Identifier.Uuid)
            .IsRequired();

        builder.Property(e => e.SearchQuery)
            .HasColumnType(DbConstants.ColumnTypes.Text.Varchar255)
            .IsRequired();

        builder.Property(e => e.UserId)
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(e => e.CreatedOn)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .HasDefaultValueSql(DbConstants.ColumnTypes.DefaultValueSql.Now)
            .IsRequired();

        builder.Property(e => e.UpdatedOn)
            .HasColumnType(DbConstants.ColumnTypes.DateTime.TimestampTz)
            .IsRequired(false);

        builder.Property(e => e.AdditionalParameters)
            .HasColumnType(DbConstants.ColumnTypes.Json.Jsonb)
            .IsRequired(false);

        builder.Property(e => e.SelectedColumns)
            .HasColumnType(DbConstants.ColumnTypes.Json.Jsonb)
            .IsRequired(false)
            .HasConversion<StringValueConverter<IList<QueryColumn>, List<QueryColumn>>>()
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.Property(e => e.ScheduledExecutionId)
            .IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Property);

        builder.HasOne(e => e.User)
            .WithMany("ScheduledEventQueries")
            .HasForeignKey(e => e.UserId)
            .HasPrincipalKey(a => a.Id);

        builder.HasOne(e => e.ScheduledExecution)
            .WithMany()
            .HasForeignKey(e => e.ScheduledExecutionId)
            .HasPrincipalKey(s => s.Id)
            .IsRequired();
    }
}
