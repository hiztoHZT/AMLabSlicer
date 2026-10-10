namespace AMLabSlicer.EngineHost;

public static class ShutdownEndpoint
{
    public static void MapEngineHostShutdown(this WebApplication app)
    {
        app.MapPost("/shutdown", (HttpContext context, IHostApplicationLifetime lifetime) =>
        {
            // Acknowledge before stopping Kestrel, so the UI receives a successful reply.
            context.Response.OnCompleted(() =>
            {
                lifetime.StopApplication();
                return Task.CompletedTask;
            });
            return Results.Ok(new { status = "stopping" });
        });
    }
}
