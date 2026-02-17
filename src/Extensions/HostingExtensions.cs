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
                if (TryGetEnvironmentVariable("ECOFLOW_APP_API_URI", out var ecoFlowAppApiUri))
                    configuration.AppApiUri = new Uri(ecoFlowAppApiUri);

                if (TryGetEnvironmentVariable("ECOFLOW_OPEN_API_URI", out var ecoFlowOpenApiUri))
                    configuration.OpenApiUri = new Uri(ecoFlowOpenApiUri);
            });
        }

        public IServiceCollection ConfigureEcoFlowAuthentication()
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
                    Console.WriteLine($"Set [{EcoFlowPrefix + accessKeyEnvironmentVariable} and {EcoFlowPrefix + secretKeyEnvironmentVariable}] or [{EcoFlowPrefix + usernameEnvironmentVariable} and {EcoFlowPrefix + passwordEnvironmentVariable}] environment variables.");
                    Environment.Exit(1);
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
