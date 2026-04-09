using Consul;

namespace SentinelService.Services;

public sealed class ConsulDiscoveryService
{
    private readonly IConsulClient _consulClient;

    public ConsulDiscoveryService(IConsulClient consulClient)
    {
        _consulClient = consulClient;
    }

    public async Task<string> GetServiceUrlAsync(string serviceName)
    {
        var result = await _consulClient.Health.Service(serviceName, tag: "", passingOnly: true);

        var instance = result.Response.FirstOrDefault()
                       ?? throw new InvalidOperationException(
                           $"No healthy instance found for service '{serviceName}'");

        var address = instance.Service.Address;
        var port    = instance.Service.Port;

        return $"https://{address}:{port}";
    }
}