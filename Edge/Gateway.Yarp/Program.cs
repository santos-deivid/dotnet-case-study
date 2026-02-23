using System.Security.Cryptography.X509Certificates;
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

// mTLS — Certificado do Gateway
builder.Services
    .AddReverseProxy()
    .ConfigureHttpClient((context, handler) =>
    {
        var certPath = builder.Configuration["Mtls:GatewayCertPath"]!;
        var certPass = builder.Configuration["Mtls:GatewayCertPassword"]!;
        var caPath   = builder.Configuration["Mtls:CaCertPath"]!;

        var gatewayCert = new X509Certificate2(certPath, certPass);

        // Lê o conteúdo PEM do arquivo e cria o certificado da CA
        var caPem = File.ReadAllText(caPath);
        var caCert = X509Certificate2.CreateFromPem(caPem);

        handler.SslOptions.ClientCertificates = [gatewayCert];
        handler.SslOptions.RemoteCertificateValidationCallback = (_, cert, chain, _) =>
        {
            if (cert is null || chain is null) return false;

            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(caCert);
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

            var result = chain.Build(new X509Certificate2(cert));

            if (!result)
                foreach (var status in chain.ChainStatus)
                    Console.WriteLine($"[mTLS] Chain error: {status.StatusInformation}");

            return result;
        };
    });

// Build
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapReverseProxy();

app.Run();