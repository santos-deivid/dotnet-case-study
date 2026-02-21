using Consul;
using Gateway.Yarp.Consul;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Authentication — Keycloak JWT Bearer
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Keycloak:Authority"];
        options.Audience = builder.Configuration["Keycloak:ClientId"];
        options.RequireHttpsMetadata =
            builder.Configuration.GetValue<bool>("Keycloak:RequireHttpsMetadata");

        options.TokenValidationParameters = new()
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    });

builder.Services.AddAuthorization();

// Consul Client
builder.Services.AddSingleton<IConsulClient>(_ =>
    new ConsulClient(config =>
    {
        config.Address = new Uri(builder.Configuration["Consul:Host"]!);
    }));

// YARP + Consul Config Provider
builder.Services.AddSingleton<ConsulProxyConfigProvider>();
builder.Services.AddSingleton<IProxyConfigProvider>(
    sp => sp.GetRequiredService<ConsulProxyConfigProvider>());

builder.Services.AddReverseProxy();

// Build
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapReverseProxy();

app.Run();