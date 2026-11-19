using AMLabSlicer.Engine.Atom;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(
    int.TryParse(Environment.GetEnvironmentVariable("AMLAB_ENGINE_PORT"), out var managedPort) ? managedPort : builder.Configuration.GetValue<int?>("Atom:Port") ?? 50101, port => port.Protocols = HttpProtocols.Http2));
builder.Services.AddGrpc(options => { options.MaxReceiveMessageSize = null; options.MaxSendMessageSize = null; });
builder.Services.AddSingleton<AtomRunner>();
var app = builder.Build();
app.MapGrpcService<AtomService>();
await app.RunAsync();
