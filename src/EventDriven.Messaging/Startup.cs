using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventDriven.Messaging;

public static class Startup
{
    /// <summary>
    /// Creates the schema (EnsureCreated builds the database and tables from the model), retrying
    /// until PostgreSQL is reachable — the database container may still be starting.
    /// </summary>
    public static async Task MigrateAsync<TContext>(IServiceProvider services, int attempts = 30)
        where TContext : DbContext
    {
        for (var i = 1; ; i++)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TContext>();
                await db.Database.EnsureCreatedAsync();
                return;
            }
            catch when (i < attempts)
            {
                await Task.Delay(1000);
            }
        }
    }
}
