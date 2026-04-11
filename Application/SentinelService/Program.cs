using System.Security.Cryptography.X509Certificates;
using Consul;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using SentinelService.Registration;
using SentinelService.Services;

var builder = WebApplication.CreateBuilder(args);

// Kestrel — mTLS
builder.WebHost.ConfigureKestrel(options =>
{
    var certPass = builder.Configuration["Mtls:ServiceCertPassword"]!;
    var serverCert = new X509Certificate2("/certs/sentinel.pfx", certPass);
    var caCert = new X509Certificate2("/certs/ca.crt");

    options.ListenAnyIP(5001);

    options.ListenAnyIP(5011, listenOptions =>
    {
        listenOptions.UseHttps(serverCert, https =>
        {
            https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
            https.ClientCertificateValidation = (cert, chain, errors) =>
            {
                if (chain is null) return false;
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(caCert);

                var result = chain.Build(cert);

                return result;
            };
        });
    });
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Keycloak:Authority"];
        options.Audience = builder.Configuration["Keycloak:ClientId"];
        options.RequireHttpsMetadata =
            builder.Configuration.GetValue<bool>("Keycloak:RequireHttpsMetadata");

        // CA própria para validar o certificado do Keycloak
        var caCert = new X509Certificate2("/certs/ca.crt");
        options.BackchannelHttpHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, chain, _) =>
            {
                if (cert is null || chain is null) return false;
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(caCert);
                return chain.Build(new X509Certificate2(cert));
            }
        };

        options.TokenValidationParameters = new()
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    });

builder.Services.AddAuthorization();

// HttpClient — Keycloak
builder.Services.AddHttpClient("keycloak")
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var caCert = new X509Certificate2("/certs/ca.crt");
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (_, cert, chain, _) =>
        {
            if (cert is null || chain is null) return false;
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(caCert);
            return chain.Build(new X509Certificate2(cert));
        };
        return handler;
    });

// HttpClient — AuditService (com mTLS)
builder.Services.AddHttpClient("audit-service")
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var certPass = builder.Configuration["Mtls:ServiceCertPassword"]!;
        var sentinelCert = new X509Certificate2("/certs/sentinel.pfx", certPass);
        var caPem = File.ReadAllText("/certs/ca.crt");
        var caCert = X509Certificate2.CreateFromPem(caPem);

        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(sentinelCert);
        handler.ServerCertificateCustomValidationCallback = (_, cert, chain, _) =>
        {
            chain!.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(caCert);
            return chain.Build(new X509Certificate2(cert!));
        };
        return handler;
    });

// Services
builder.Services.AddScoped<KeycloakTokenService>();

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
                return chain.Build(new X509Certificate2(cert!));
            };
        }
    );
});

// Consul Registration
builder.Services.AddHostedService<ConsulRegistrationService>();

builder.Services.AddScoped<ConsulDiscoveryService>();

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