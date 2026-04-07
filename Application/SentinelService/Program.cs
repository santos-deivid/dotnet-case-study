using System.Security.Cryptography.X509Certificates;
using Consul;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using SentinelService.Registration;
using SentinelService.Services;

var builder = WebApplication.CreateBuilder(args);

// Kestrel — mTLS
builder.WebHost.ConfigureKestrel(options =>
{
    var serverCert = new X509Certificate2("/certs/sentinel.pfx", "sentinel123");
    var caCert = new X509Certificate2("/certs/ca.crt");

    options.ListenAnyIP(5001);

    options.ListenAnyIP(5011, listenOptions =>
    {
        listenOptions.UseHttps(serverCert, https =>
        {
            https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
            https.ClientCertificateValidation = (cert, chain, errors) =>
            {
                Console.WriteLine($"[mTLS Server] Received cert: {cert?.Subject ?? "NULL"}");
                Console.WriteLine($"[mTLS Server] Errors: {errors}");

                if (cert is null || chain is null)
                {
                    Console.WriteLine("[mTLS Server] Cert or chain is null — REJECTED");
                    return false;
                }

                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(caCert);
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

                var result = chain.Build(cert);
                Console.WriteLine($"[mTLS Server] Chain build result: {result}");

                foreach (var status in chain.ChainStatus)
                    Console.WriteLine($"[mTLS Server] Chain status: {status.StatusInformation}");

                return result;
            };
        });
    });
});

// HttpClient — Keycloak (sem mTLS)
builder.Services.AddHttpClient("keycloak");

// HttpClient — AuditService (com mTLS)
builder.Services.AddHttpClient("audit-service")
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var sentinelCert = new X509Certificate2("/certs/sentinel.pfx", "sentinel123");
        var caPem = File.ReadAllText("/certs/ca.crt");
        var caCert = X509Certificate2.CreateFromPem(caPem);

        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(sentinelCert);
        handler.ServerCertificateCustomValidationCallback = (_, cert, chain, _) =>
        {
            chain!.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(caCert);
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
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
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
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

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();