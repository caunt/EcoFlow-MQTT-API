using EcoFlow.Mqtt.Api.Configuration.Authentication;

namespace EcoFlow.Mqtt.Api.Configuration;

public class EcoFlowConfiguration
{
    public Uri AppApiUri { get; set; } = new("https://api.ecoflow.com");
    public Uri OpenApiUri { get; set; } = new("https://api-e.ecoflow.com");
    public IAuthentication[] Authentications { get; set; } = [];
    public bool VerboseLogging { get; set; }
    public TimeSpan PollingInterval { get; set; }
}
