using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace AMLabSlicer.EngineHost;

public static class ShutdownEndpoint
{
    public static void MapEngineHostShutdown(this WebApplication app, string instance)
    {
        var owners = new ConcurrentDictionary<string, byte>();
        app.MapPost("/owner", (HttpContext context, IHostApplicationLifetime lifetime) =>
        {
            if (context.Request.Headers["x-amlab-host-instance"].ToString() != instance)
                return Results.Conflict(new { error = "Host instance changed or missing." });
            if (!int.TryParse(context.Request.Headers["x-amlab-ui-pid"], out var pid) ||
                !long.TryParse(context.Request.Headers["x-amlab-ui-start"], out var started) || pid == Environment.ProcessId)
                return Results.BadRequest();
            Process owner;
            try
            {
                owner = Process.GetProcessById(pid);
                if (owner.HasExited || owner.StartTime.ToUniversalTime().Ticks != started)
                { owner.Dispose(); return Results.Conflict(new { error = "UI process changed or exited." }); }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            { return Results.Conflict(new { error = "UI process unavailable." }); }
            var key = pid.ToString(CultureInfo.InvariantCulture) + ":" + started;
            if (!owners.TryAdd(key, 0)) owner.Dispose();
            else _ = WatchOwnerAsync(owner, lifetime);
            return Results.Ok();
        });
        app.MapPost("/shutdown", (HttpContext context, IHostApplicationLifetime lifetime) =>
        {
            if (context.Request.Headers["x-amlab-host-instance"].ToString() != instance)
                return Results.Conflict(new { error = "Host instance changed or missing." });
            context.Response.OnCompleted(() =>
            {
                lifetime.StopApplication();
                return Task.CompletedTask;
            });
            return Results.Ok(new { status = "stopping" });
        });
    }

    private static async Task WatchOwnerAsync(Process owner, IHostApplicationLifetime lifetime)
    {
        using (owner)
        {
            try
            {
                await owner.WaitForExitAsync(lifetime.ApplicationStopping);
                Console.WriteLine($"[EngineHost] UI {owner.Id} exited; stopping host and engines.");
                lifetime.StopApplication();
            }
            catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested) { }
        }
    }
}
