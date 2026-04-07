using System.Security.Cryptography.X509Certificates;
using AuditService.Registration;
using Consul;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Server.Kestrel.Https;

var builder = WebApplication.CreateBuilder(args);

// Kestrel — mTLS
builder.WebHost.ConfigureKestrel(options =>
{
    var serverCert = new X509Certificate2("/certs/audit.pfx", "audit123");
    var caCert = new X509Certificate2("/certs/ca.crt");

    // HTTP — apenas para health check do Consul
    options.ListenAnyIP(5002);

    // HTTPS com mTLS — para tráfego real
    options.ListenAnyIP(5012, listenOptions =>
    {
        listenOptions.UseHttps(serverCert, https =>
        {
            https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
            https.ClientCertificateValidation = (cert, chain, _) =>
            {
                chain!.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(caCert);
                return chain.Build(cert);
            };
        });
    });
});

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
{
    var caCert = new X509Certificate2("/certs/ca.crt");

    return new ConsulClient(config => { config.Address = new Uri(builder.Configuration["Consul:Host"]!); },
        null,
        handlerOverride: handler =>
        {
            // Certificado do serviço para autenticar no Consul
            var servicePfxPath = builder.Configuration["Mtls:ServiceCertPath"]!;
            var servicePfxPass = builder.Configuration["Mtls:ServiceCertPassword"]!;
            var serviceCert = new X509Certificate2(servicePfxPath, servicePfxPass);

            handler.ClientCertificates.Add(serviceCert);
            handler.ServerCertificateCustomValidationCallback = (_, cert, chain, _) =>
            {
                chain!.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(caCert);
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                return chain.Build(new X509Certificate2(cert!));
            };
        }
    );
});

// Consul Registration
builder.Services.AddHostedService<ConsulRegistrationService>();

// Controllers + Health Check
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// Build
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();