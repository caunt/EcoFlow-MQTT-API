using EcoFlow.Mqtt.Api.Configuration;
using EcoFlow.Mqtt.Api.Configuration.Authentication;
using EcoFlow.Mqtt.Api.Exceptions;
using EcoFlow.Mqtt.Api.Models;
using EcoFlow.Mqtt.Api.Session;
using Microsoft.Extensions.Options;
using Nito.Disposables.Internals;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace EcoFlow.Mqtt.Api.Services;

public class InternalHttpApi(IOptions<EcoFlowConfiguration> options, HttpClient httpClient)
{
    public async Task<MqttConfiguration> GetMqttConfigurationAsync(ISession session, CancellationToken cancellationToken = default)
    {
        return session switch
        {
            AppSession appSession => await GetMqttConfigurationCoreAsync("/iot-auth/app/certification", request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appSession.Token)),
            OpenSession openSession => await GetMqttConfigurationCoreAsync("/iot-open/sign/certification", openSession.SignRequest),
            _ => throw new NotSupportedException($"Unsupported session type: {session}")
        };

        async Task<MqttConfiguration> GetMqttConfigurationCoreAsync(string path, Action<HttpRequestMessage> signRequest)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(options.Value.AppApiUri, path));
            signRequest(request);

            var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var node = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);

            var url = node?["data"]?["url"]?.GetValue<string>();
            var port = int.Parse(node?["data"]?["port"]?.GetValue<string>() ?? "0");
            var username = node?["data"]?["certificateAccount"]?.GetValue<string>();
            var password = node?["data"]?["certificatePassword"]?.GetValue<string>();
            var tls = node?["data"]?["protocol"]?.GetValue<string>() switch
            {
                "mqtt" => false,
                "mqtts" => true,
                _ => throw new MqttConfigurationException($"Invalid MQTT configuration received: {node}")
            };

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                throw new MqttConfigurationException($"Invalid MQTT configuration received: {node}");

            return new MqttConfiguration(tls, url, port, username, password);
        }
    }

    public async Task<string[]> GetDevicesAsync(ISession session, CancellationToken cancellationToken = default)
    {
        return session switch
        {
            AppSession appSession => await GetDevicesCoreAsync("/app/user/device", request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appSession.Token)),
            OpenSession openSession => await GetDevicesCoreAsync("/iot-open/sign/device/list", openSession.SignRequest),
            _ => throw new NotSupportedException($"Unsupported session type: {session}")
        };

        async Task<string[]> GetDevicesCoreAsync(string path, Action<HttpRequestMessage> signRequest)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(options.Value.AppApiUri, path));
            signRequest(request);

            var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var node = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
            var devices = node?["data"] switch
            {
                JsonArray jsonArray => jsonArray.Select(value => value?["sn"]?.GetValue<string>()).WhereNotNull(),
                JsonObject jsonObject => jsonObject.Select(category => category.Value?.AsObject()).WhereNotNull().SelectMany(devices => devices.Select(device => device.Key)),
                _ => throw new DeviceListException($"Invalid device list received: {node}")
            };

            return [.. devices];
        }
    }

    public async Task<ISession> AuthenticateAsync(IAuthentication authentication, CancellationToken cancellationToken = default)
    {
        switch (authentication)
        {
            case AppAuthentication appAuthentication:
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.Value.AppApiUri, "/auth/login"));
                    request.Content = JsonContent.Create(new
                    {
                        os = "linux",
                        scene = "IOT_APP",
                        appVersion = "1.0.0",
                        osVersion = "5.15.90.1-kali-fake",
                        password = Convert.ToBase64String(Encoding.UTF8.GetBytes(appAuthentication.Password)),
                        oauth = new { bundleId = "com.ef.EcoFlow" },
                        email = appAuthentication.Username,
                        userType = "ECOFLOW"
                    });

                    var response = await httpClient.SendAsync(request, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var node = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);

                    var token = node?["data"]?["token"]?.GetValue<string>();
                    var userId = node?["data"]?["user"]?["userId"]?.GetValue<string>();

                    if (string.IsNullOrEmpty(token))
                        throw new AuthenticationException($"No token received from API: {node}");

                    if (string.IsNullOrEmpty(userId))
                        throw new AuthenticationException($"No user id received from API: {node}");

                    return new AppSession(token, new User(userId));
                }
            case OpenAuthentication openAuthentication:
                return new OpenSession(openAuthentication);
            default:
                throw new NotSupportedException($"Unsupported authentication method: {authentication}");
        }
    }
}
