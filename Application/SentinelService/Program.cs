using System.Security.Cryptography.X509Certificates;
using Consul;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using SentinelService.Registration;

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