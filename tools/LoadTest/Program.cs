using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

// Load test: fire N orders at concurrency C, measure ingest throughput and POST latency percentiles,
// then measure end-to-end latency (place -> Shipped) for a sample as it drains through all three services.
//
//   loadtest [--orders 500] [--concurrency 32] [--amount 250] [--url http://localhost:8080]

var opt = Args(args);
using var http = new HttpClient
{
    BaseAddress = new Uri(opt.Url),
    Timeout = TimeSpan.FromSeconds(20)
};

Console.WriteLine($"Load test — {opt.Orders} orders, concurrency {opt.Concurrency}, amount {opt.Amount}, {opt.Url}\n");

var postLatencies = new ConcurrentBag<double>();
var placed = new ConcurrentBag<(Guid id, long postTicks)>();
var failures = 0;

var sw = Stopwatch.StartNew();
await Parallel.ForEachAsync(Enumerable.Range(0, opt.Orders),
    new ParallelOptions { MaxDegreeOfParallelism = opt.Concurrency },
    async (_, ct) =>
    {
        var t0 = Stopwatch.GetTimestamp();
        try
        {
            var resp = await http.PostAsJsonAsync("/orders", new { customer = "load", amount = opt.Amount }, ct);
            var elapsed = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            postLatencies.Add(elapsed);
            if (resp.IsSuccessStatusCode)
            {
                var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
                if (doc.TryGetProperty("id", out var idProp) && Guid.TryParse(idProp.GetString(), out var id))
                    placed.Add((id, Stopwatch.GetTimestamp()));
            }
            else Interlocked.Increment(ref failures);
        }
        catch { Interlocked.Increment(ref failures); }
    });
sw.Stop();

var ingestSeconds = sw.Elapsed.TotalSeconds;
var throughput = opt.Orders / ingestSeconds;

Console.WriteLine("Ingest (POST /orders)");
Console.WriteLine($"  placed:      {placed.Count}/{opt.Orders}  (failures {failures})");
Console.WriteLine($"  duration:    {ingestSeconds:0.00}s");
Console.WriteLine($"  throughput:  {throughput:0} orders/s");
Report("  POST latency", postLatencies.ToArray());

// End-to-end for a sample: poll until Shipped, measure from POST completion.
var sample = placed.OrderBy(_ => Guid.NewGuid()).Take(Math.Min(60, placed.Count)).ToList();
Console.WriteLine($"\nEnd-to-end (place -> Shipped), sample of {sample.Count}");
var e2e = new ConcurrentBag<double>();
await Parallel.ForEachAsync(sample, new ParallelOptions { MaxDegreeOfParallelism = 16 }, async (item, ct) =>
{
    var deadline = Stopwatch.GetTimestamp() + (long)(60 * Stopwatch.Frequency);
    while (Stopwatch.GetTimestamp() < deadline)
    {
        try
        {
            var o = await http.GetFromJsonAsync<JsonElement>($"/orders/{item.id}", ct);
            var status = o.GetProperty("status").GetString();
            if (status is "Shipped" or "PaymentFailed")
            {
                e2e.Add(Stopwatch.GetElapsedTime(item.postTicks).TotalMilliseconds);
                return;
            }
        }
        catch { }
        await Task.Delay(150, ct);
    }
});
Console.WriteLine($"  completed:   {e2e.Count}/{sample.Count}");
Report("  e2e latency ", e2e.ToArray());

Console.WriteLine("\nDone.");
return 0;

static void Report(string label, double[] xs)
{
    if (xs.Length == 0) { Console.WriteLine($"{label}: no samples"); return; }
    Array.Sort(xs);
    double P(double q) => xs[Math.Clamp((int)Math.Ceiling(q * xs.Length) - 1, 0, xs.Length - 1)];
    Console.WriteLine($"{label}: p50 {P(.50):0} ms · p95 {P(.95):0} ms · p99 {P(.99):0} ms · max {xs[^1]:0} ms");
}

static Options Args(string[] a)
{
    var m = new Dictionary<string, string>();
    for (var i = 0; i + 1 < a.Length; i += 2) m[a[i].TrimStart('-')] = a[i + 1];
    return new Options(
        Orders: int.TryParse(m.GetValueOrDefault("orders"), out var n) ? n : 500,
        Concurrency: int.TryParse(m.GetValueOrDefault("concurrency"), out var c) ? c : 32,
        Amount: decimal.TryParse(m.GetValueOrDefault("amount"), System.Globalization.CultureInfo.InvariantCulture, out var amt) ? amt : 250m,
        Url: m.GetValueOrDefault("url", "http://localhost:8080"));
}

record Options(int Orders, int Concurrency, decimal Amount, string Url);
