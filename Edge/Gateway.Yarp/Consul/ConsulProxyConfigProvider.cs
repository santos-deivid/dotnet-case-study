using Consul;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using DestinationConfig = Yarp.ReverseProxy.Configuration.DestinationConfig;
using RouteConfig = Yarp.ReverseProxy.Configuration.RouteConfig;

namespace Gateway.Yarp.Consul;

public class ConsulProxyConfigProvider : IProxyConfigProvider, IDisposable
{
    private readonly IConsulClient _consulClient;
    private readonly ILogger<ConsulProxyConfigProvider> _logger;
    private ConsulProxyConfig _config;
    private readonly Timer _timer;

    public ConsulProxyConfigProvider(IConsulClient consulClient, IConfiguration configuration, ILogger<ConsulProxyConfigProvider> logger)
    {
        _consulClient = consulClient;
        _logger = logger;
        var refreshInterval = configuration.GetValue("Consul:RefreshIntervalSeconds", 15);
        _config = new ConsulProxyConfig([], []);

        _timer = new Timer(
            callback: async void (_) => await RefreshAsync(),
            state: null,
            dueTime: TimeSpan.Zero,
            period: TimeSpan.FromSeconds(refreshInterval));
    }

    public IProxyConfig GetConfig() => _config;

    private async Task RefreshAsync()
    {
        try
        {
            var catalogResponse = await _consulClient.Catalog.Services();
            
            var routes = new List<RouteConfig>();
            var clusters = new List<ClusterConfig>();

            foreach (var service in catalogResponse.Response)
            {
                var serviceName = service.Key;

                if (serviceName == "consul") continue;
                
                var instancesResponse = await _consulClient.Health.Service(serviceName, tag: "", passingOnly: true);
                var instances = instancesResponse.Response
                    .Select(e => e.Service)
                    .ToArray();
                
                if (instances.Length == 0) continue;

                var destinations = instances
                    .Select((instance, index) => KeyValuePair.Create(
                        $"{serviceName}-{index}",
                        new DestinationConfig()
                        {
                            Address = $"https://{instance.Address}:{instance.Port}"
                        }))
                    .ToDictionary();
                
                clusters.Add(new ClusterConfig
                {
                    ClusterId = serviceName,
                    Destinations = destinations,
                    HttpClient = new HttpClientConfig
                    {
                        RequestHeaderEncoding = "utf-8"
                    },
                    HttpRequest = new ForwarderRequestConfig
                    {
                        Version = new Version(1, 1),
                        VersionPolicy = HttpVersionPolicy.RequestVersionExact
                    }
                });

                routes.Add(new RouteConfig
                {
                    RouteId = $"{serviceName}-route",
                    ClusterId = serviceName,
                    AuthorizationPolicy = "default",
                    Match = new RouteMatch
                    {
                        Path = $"/api/{serviceName}/{{**catch-all}}"
                    },
                    Transforms = new List<IReadOnlyDictionary<string, string>>
                    {
                        new Dictionary<string, string>
                        {
                            ["PathPattern"] = "/{**catch-all}"
                        }
                    }
                });

                _logger.LogInformation(
                    "Registered route for service '{ServiceName}' with {Count} instance(s)",
                    serviceName, instances.Length);
            }
            
            var previous = _config;
            _config = new ConsulProxyConfig(routes, clusters);
            previous.SignalChange();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh proxy configuration from Consul");
        }
    }

    public void Dispose() => _timer.Dispose();
}