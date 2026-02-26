using EcoFlow.Mqtt.Api.Configuration;
using EcoFlow.Mqtt.Api.Configuration.Authentication;
using EcoFlow.Mqtt.Api.Exceptions;
using EcoFlow.Mqtt.Api.Json;
using EcoFlow.Mqtt.Api.Models;
using EcoFlow.Mqtt.Api.Session;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using Nito.Disposables.Internals;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace EcoFlow.Mqtt.Api.Services;

public class InternalHttpApi(IOptions<EcoFlowConfiguration> options, HttpClient httpClient)
{
    internal record EcoFlowOAuthBundle(
        [property: JsonPropertyName("bundleId")] string BundleId
    );

    internal record EcoFlowAuthenticationPayload(
        [property: JsonPropertyName("os")] string Os,
        [property: JsonPropertyName("scene")] string Scene,
        [property: JsonPropertyName("appVersion")] string AppVersion,
        [property: JsonPropertyName("osVersion")] string OsVersion,
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("oauth")] EcoFlowOAuthBundle Oauth,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("userType")] string UserType
    );

    private SerialNumberSet? _serialNumberSet;
    private readonly AsyncLock _serialNumberSetLock = new();

    public async Task<DeviceInfo> GetDeviceInfoAsync(ISession session, string serialNumber, CancellationToken cancellationToken = default)
    {
        var serialNumberSet = await GetDeviceSerialNumberTemplatesAsync(cancellationToken);
        return await serialNumberSet.GetDeviceInfoAsync(session, serialNumber, cancellationToken);
    }

    public async Task<SerialNumberSet> GetDeviceSerialNumberTemplatesAsync(CancellationToken cancellationToken = default)
    {
        using var disposable = await _serialNumberSetLock.LockAsync(cancellationToken);

        if (_serialNumberSet is null)
        {
            const string token = "Ecoflow20230911@"; // com.ecoflow.iot.p193ui.viewmodel.MainViewModel
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(options.Value.AppApiUri, "/app/sku/getAllRealSnList?token=" + token));

            var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var node = await response.Content.ReadFromJsonAsync(ApplicationJsonContext.Default.JsonNode, cancellationToken);
            _serialNumberSet = new SerialNumberSet(options, httpClient, node?["data"]?.AsArray().Select(child => child?["sn"]?.GetValue<string>()).WhereNotNull().ToArray() ?? []);
        }

        return _serialNumberSet;
    }

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

            var node = await response.Content.ReadFromJsonAsync(ApplicationJsonContext.Default.JsonNode, cancellationToken);

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

    public async Task<DeviceInfo[]> GetDevicesAsync(ISession session, CancellationToken cancellationToken = default)
    {
        return session switch
        {
            AppSession appSession => await GetDevicesCoreAsync("/app/user/device", request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appSession.Token)),
            OpenSession openSession => await GetDevicesCoreAsync("/iot-open/sign/device/list", openSession.SignRequest),
            _ => throw new NotSupportedException($"Unsupported session type: {session}")
        };

        async Task<DeviceInfo[]> GetDevicesCoreAsync(string path, Action<HttpRequestMessage> signRequest)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(options.Value.AppApiUri, path));
            signRequest(request);

            var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var node = await response.Content.ReadFromJsonAsync(ApplicationJsonContext.Default.JsonNode, cancellationToken);
            var deviceSerialNumbers = node?["data"] switch
            {
                JsonArray jsonArray => jsonArray.Select(value => value?["sn"]?.GetValue<string>()).WhereNotNull(),
                JsonObject jsonObject => jsonObject.Select(category => category.Value?.AsObject()).WhereNotNull().SelectMany(devices => devices.Select(device => device.Key)),
                _ => throw new DeviceListException($"Invalid device list received: {node}")
            };

            var devices = await deviceSerialNumbers.Select(async deviceSerialNumber => await GetDeviceInfoAsync(session, deviceSerialNumber, cancellationToken)).WhenAll();

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
                    request.Content = JsonContent.Create(
                        new EcoFlowAuthenticationPayload(
                            Os: "linux",
                            Scene: "IOT_APP",
                            AppVersion: "1.0.0",
                            OsVersion: "5.15.90.1-kali-fake",
                            Password: Convert.ToBase64String(Encoding.UTF8.GetBytes(appAuthentication.Password)),
                            Oauth: new EcoFlowOAuthBundle("com.ef.EcoFlow"),
                            Email: appAuthentication.Username,
                            UserType: "ECOFLOW"
                        ),
                        ApplicationJsonContext.Default.EcoFlowAuthenticationPayload);

                    var response = await httpClient.SendAsync(request, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var node = await response.Content.ReadFromJsonAsync(ApplicationJsonContext.Default.JsonNode, cancellationToken);

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
