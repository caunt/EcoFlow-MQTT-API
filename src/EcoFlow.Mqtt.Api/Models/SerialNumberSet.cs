using EcoFlow.Mqtt.Api.Configuration;
using EcoFlow.Mqtt.Api.Json;
using EcoFlow.Mqtt.Api.Session;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;

namespace EcoFlow.Mqtt.Api.Models;

public class SerialNumberSet(IOptions<EcoFlowConfiguration> options, HttpClient httpClient, ReadOnlyMemory<string> templates)
{
    private readonly AsyncLock _lock = new();
    private readonly Dictionary<string, DeviceInfo> _cache = [];

    public async Task<DeviceInfo> GetDeviceInfoAsync(ISession session, string serialNumber, CancellationToken cancellationToken = default)
    {
        using var disposable = await _lock.LockAsync(cancellationToken);

        if (!_cache.TryGetValue(serialNumber, out var deviceInfo))
        {
            if (!TryMatch(serialNumber, out var pattern))
                throw new InvalidOperationException($"Serial number '{serialNumber}' does not match any known patterns.");

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(options.Value.AppApiUri, "/app/sku/queryBySnPrefix?sn=" + pattern));

            var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var node = await response.Content.ReadFromJsonAsync(ApplicationJsonContext.Default.JsonNode, cancellationToken);
            var data = node?["data"]?.AsArray() ?? [];

            if (data.Count is 0)
                throw new InvalidOperationException($"No device information found for serial number '{serialNumber}'.");

            if (data.Count > 1)
                throw new InvalidOperationException($"Multiple device information entries found for serial number '{serialNumber}'.\n{data}");

            var title = data[0]?["name"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(title))
                throw new InvalidOperationException($"Device information for serial number '{serialNumber}' does not contain a valid title.\n{data[0]}");

            _cache[serialNumber] = deviceInfo = new DeviceInfo(session, serialNumber, title);
        }

        return deviceInfo;
    }

    public bool TryMatch(string serialNumber, [MaybeNullWhen(false)] out string pattern)
    {
        pattern = null;

        if (serialNumber is null)
            return false;

        var matchCount = 0;
        string? potentialMatch = null;

        foreach (var template in templates.Span)
        {
            if (template is null)
                continue;

            if (serialNumber.Length != template.Length && serialNumber.Length - 4 != template.Length)
                continue;

            var isMatch = true;

            for (var characterIndex = 0; characterIndex < template.Length; characterIndex++)
            {
                if (template[characterIndex] != '-' && template[characterIndex] != serialNumber[characterIndex])
                {
                    isMatch = false;
                    break;
                }
            }

            if (isMatch)
            {
                matchCount++;
                potentialMatch = template;

                if (matchCount > 1)
                    return false;
            }
        }

        if (matchCount is 1)
        {
            pattern = potentialMatch?.TrimEnd('-');
            return pattern is not null;
        }

        return false;
    }
}