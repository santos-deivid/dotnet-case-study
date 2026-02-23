using System.Security.Cryptography.X509Certificates;
using AuditService.Registration;
using Consul;
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