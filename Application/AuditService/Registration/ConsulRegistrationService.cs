using Consul;

namespace AuditService.Registration;

public sealed class ConsulRegistrationService(
    IConsulClient consulClient,
    IConfiguration configuration,
    ILogger<ConsulRegistrationService> logger)
    : IHostedService
{
    private string _serviceId = string.Empty;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var serviceName = configuration["Consul:ServiceName"]!;
        var serviceAddress = configuration["Consul:ServiceAddress"]!;
        var servicePort = configuration.GetValue<int>("Consul:ServicePort");
        var healthInterval = configuration.GetValue<int>("Consul:HealthCheckIntervalSeconds", 10);
        var healthTimeout = configuration.GetValue<int>("Consul:HealthCheckTimeoutSeconds", 5);

        _serviceId = $"{serviceName}-{Guid.NewGuid()}";

        var registration = new AgentServiceRegistration
        {
            ID = _serviceId,
            Name = serviceName,
            Address = serviceAddress,
            Port = servicePort,
            Tags = ["dotnet", "api"],
            Check = new AgentServiceCheck
            {
                HTTP = $"http://host.docker.internal:{servicePort}/health",
                Interval = TimeSpan.FromSeconds(healthInterval),
                Timeout = TimeSpan.FromSeconds(healthTimeout),
                DeregisterCriticalServiceAfter = TimeSpan.FromSeconds(30)
            }
        };

        await consulClient.Agent.ServiceRegister(registration, cancellationToken);

        logger.LogInformation(
            "Service '{ServiceName}' registered in Consul with ID '{ServiceId}' at {Address}:{Port}",
            serviceName, _serviceId, serviceAddress, servicePort);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await consulClient.Agent.ServiceDeregister(_serviceId, cancellationToken);

        logger.LogInformation(
            "Service '{ServiceId}' deregistered from Consul",
            _serviceId);
    }
}