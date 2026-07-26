using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EventDriven.Messaging;

public static class MessagingRegistration
{
    public static IServiceCollection AddEventDrivenMessaging(this IServiceCollection services, Action<MessagingOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<RabbitConnection>();
        return services;
    }

    /// <summary>Runs the outbox dispatcher for this service's DbContext (ADR-0003).</summary>
    public static IServiceCollection AddOutboxDispatcher<TContext>(this IServiceCollection services)
        where TContext : DbContext, IMessagingDbContext
    {
        services.AddHostedService<OutboxDispatcher<TContext>>();
        return services;
    }

    /// <summary>A scoped outbox writer for producers (e.g. an API handler) to publish within their transaction.</summary>
    public static IServiceCollection AddOutboxWriter<TContext>(this IServiceCollection services)
        where TContext : DbContext, IMessagingDbContext
    {
        services.AddScoped(sp => new OutboxWriter(
            sp.GetRequiredService<TContext>(),
            sp.GetRequiredService<IOptions<MessagingOptions>>().Value.EventExchange));
        return services;
    }

    /// <summary>Runs an idempotent consumer with the three-layer retry strategy (ADR-0004, ADR-0005).</summary>
    public static IServiceCollection AddEventConsumer<TContext>(
        this IServiceCollection services, Action<EventConsumerOptions<TContext>> configure)
        where TContext : DbContext, IMessagingDbContext
    {
        var options = new EventConsumerOptions<TContext>();
        configure(options);
        services.AddSingleton(options);
        services.AddHostedService<ConsumerHost<TContext>>();
        return services;
    }
}
