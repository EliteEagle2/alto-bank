using System.Diagnostics;

namespace AltoBank.Middleware;

/// <summary>
/// CUSTOM MIDDLEWARE: times every request and logs one line to the console, e.g.
///   [AltoBank] GET /Dashboard -> 200 in 12 ms
/// It also adds an "X-Response-Time-ms" header to the response.
///
/// Middleware is a class with a constructor that takes the NEXT step in the pipeline
/// (RequestDelegate) and an InvokeAsync method. It's added in Program.cs with
///     app.UseMiddleware&lt;RequestTimingMiddleware&gt;();
/// </summary>
public class RequestTimingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestTimingMiddleware> _logger;

    public RequestTimingMiddleware(RequestDelegate next, ILogger<RequestTimingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        // Headers must be added before the response starts being sent.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Response-Time-ms"] = stopwatch.ElapsedMilliseconds.ToString();
            return Task.CompletedTask;
        });

        await _next(context); // run the rest of the pipeline (routing, controllers, views...)

        stopwatch.Stop();
        // Skip static files (css/js/images) so the console stays readable.
        if (!Path.HasExtension(context.Request.Path.Value))
        {
            _logger.LogInformation("[AltoBank] {Method} {Path}{Query} -> {Status} in {Elapsed} ms",
                context.Request.Method, context.Request.Path, context.Request.QueryString,
                context.Response.StatusCode, stopwatch.ElapsedMilliseconds);
        }
    }
}
