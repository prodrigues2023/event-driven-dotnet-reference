using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace EventDriven.Messaging;

public static class TelemetryRegistration
{
    /// <summary>
    /// Wires OpenTelemetry tracing for a service: the messaging spans (publish/consume) plus, for the
    /// API, ASP.NET request spans — all exported over OTLP (endpoint from OTEL_EXPORTER_OTLP_ENDPOINT).
    /// A single order's flow across services shows up as one trace in Jaeger.
    /// </summary>
    public static IServiceCollection AddEventDrivenTelemetry(
        this IServiceCollection services, string serviceName, bool aspNetCore = false)
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;

        services.AddOpenTelemetry().WithTracing(t =>
        {
            t.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName));
            t.AddSource(Telemetry.SourceName);
            if (aspNetCore) t.AddAspNetCoreInstrumentation();
            t.AddOtlpExporter();
        });
        return services;
    }
}
