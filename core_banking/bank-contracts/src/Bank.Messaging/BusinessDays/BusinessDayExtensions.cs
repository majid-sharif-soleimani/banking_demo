using Bank.Common;
using Bank.Contracts.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bank.Messaging.BusinessDays;

public static class BusinessDayExtensions
{
    /// <summary>
    /// The consumer side of Operations' business date (ADR 0016): handlers for BusinessDayOpened and DayClosed that keep
    /// the local business_day table, and IBusinessClock (scoped) that reads it. The service must also call
    /// <see cref="AddLocalBusinessDays"/> in its model, add a migration, and list "operations" in Kafka:SubscribeTo.
    /// </summary>
    public static IServiceCollection AddBusinessDayConsumer(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddEventHandler<BusinessDayOpened, LocalBusinessDayOpenedHandler>();
        services.AddEventHandler<DayClosed, LocalDayClosedHandler>();
        services.TryAddScoped<IBusinessClock, DatabaseBusinessClock>();
        return services;
    }

    /// <summary>Adds the local business_day table to the service's model, so its own migrations create it.</summary>
    public static ModelBuilder AddLocalBusinessDays(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocalBusinessDay>(day =>
        {
            day.ToTable("business_day");
            day.HasKey(d => d.Date).HasName("pk_business_day");
            day.Property(d => d.Date).HasColumnName("date");
            day.Property(d => d.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
            day.Property(d => d.CreatedAt).HasColumnName("created_at");
            day.Property(d => d.PerformedBy).HasColumnName("performed_by").HasMaxLength(PerformedBy.MaxLength);
            day.Property(d => d.Version).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        });
        return modelBuilder;
    }
}
