using AuditService.Registration;
using Consul;

var builder = WebApplication.CreateBuilder(args);

// Consul Client
builder.Services.AddSingleton<IConsulClient>(_ =>
    new ConsulClient(config =>
    {
        config.Address = new Uri(builder.Configuration["Consul:Host"]!);
    }));

// Consul Registration
builder.Services.AddHostedService<ConsulRegistrationService>();

// Controllers + Health Check
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// Build
var app = builder.Build();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();