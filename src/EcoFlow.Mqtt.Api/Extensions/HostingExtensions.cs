using EcoFlow.Mqtt.Api.Configuration;
using EcoFlow.Mqtt.Api.Configuration.Authentication;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;

namespace EcoFlow.Mqtt.Api.Extensions;

public static class HostingExtensions
{
    private const string EcoFlowPrefix = "ECOFLOW_";

    extension(IServiceCollection services)
    {
        public IServiceCollection ConfigureAnonymousHttpClient()
        {
            return services.ConfigureHttpClientDefaults(httpClientBuilder =>
            {
                httpClientBuilder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    UseCookies = false
                });
            });
        }

        public IServiceCollection ConfigureEcoFlowEndpoints()
        {
            return services.Configure<EcoFlowConfiguration>(configuration =>
            {
                if (TryGetEnvironmentVariable("APP_API_URI", out var ecoFlowAppApiUri))
                    configuration.AppApiUri = new Uri(ecoFlowAppApiUri);

                if (TryGetEnvironmentVariable("OPEN_API_URI", out var ecoFlowOpenApiUri))
                    configuration.OpenApiUri = new Uri(ecoFlowOpenApiUri);
            });
        }

        public IServiceCollection ConfigureEcoFlowPolling()
        {
            return services.Configure<EcoFlowConfiguration>(configuration =>
            {
                if (TryGetEnvironmentVariable("POLLING_INTERVAL_SECONDS", out var value) && int.TryParse(value, out var pollingIntervalSeconds))
                    configuration.PollingInterval = TimeSpan.FromSeconds(pollingIntervalSeconds);
                else
                    configuration.PollingInterval = TimeSpan.FromSeconds(15);
            });
        }

        public IServiceCollection ConfigureEcoFlowLogging()
        {
            return services.Configure<EcoFlowConfiguration>(configuration =>
            {
                if (TryGetEnvironmentVariable("VERBOSE_LOGGING", out var value) && bool.TryParse(value, out var verboseLogging))
                    configuration.VerboseLogging = verboseLogging;
            });
        }

        public IServiceCollection ConfigureEcoFlowAuthentication(Action? errorHandler = null)
        {
            return services.Configure<EcoFlowConfiguration>(configuration =>
            {
                const string accessKeyEnvironmentVariable = "ACCESS_KEY";
                const string secretKeyEnvironmentVariable = "SECRET_KEY";
                const string usernameEnvironmentVariable = "USERNAME";
                const string passwordEnvironmentVariable = "PASSWORD";

                var authentications = new List<IAuthentication>(2);

                if (TryGetEnvironmentVariable(accessKeyEnvironmentVariable, out var accessKey) && TryGetEnvironmentVariable(secretKeyEnvironmentVariable, out var secretKey))
                    authentications.Add(new OpenAuthentication(accessKey, secretKey));

                if (TryGetEnvironmentVariable(usernameEnvironmentVariable, out var username) && TryGetEnvironmentVariable(passwordEnvironmentVariable, out var password))
                    authentications.Add(new AppAuthentication(username, password));

                if (authentications.Count is 0)
                {
                    Console.WriteLine("⚠️ No authentication method configured.");
                    Console.WriteLine($"Set [{EcoFlowPrefix + usernameEnvironmentVariable} and {EcoFlowPrefix + passwordEnvironmentVariable}] or [{EcoFlowPrefix + accessKeyEnvironmentVariable} and {EcoFlowPrefix + secretKeyEnvironmentVariable}] environment variables.");

                    errorHandler?.Invoke();
                }
                else
                {
                    configuration.Authentications = [.. authentications];
                }
            });
        }

        private static bool TryGetEnvironmentVariable(string name, [MaybeNullWhen(false)] out string value)
        {
            value = Environment.GetEnvironmentVariable(EcoFlowPrefix + name);
            return value is not null;
        }
    }
}
