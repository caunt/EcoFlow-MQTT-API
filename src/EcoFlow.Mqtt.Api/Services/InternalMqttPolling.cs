using EcoFlow.Mqtt.Api.Models;
using EcoFlow.Mqtt.Api.Session;
using Microsoft.Extensions.Hosting;
using System.Globalization;

namespace EcoFlow.Mqtt.Api.Services;

public class InternalMqttPolling(InternalMqttApi mqttApi) : BackgroundService
{
    private record CallbackDisposable(Action Action) : IDisposable
    {
        public void Dispose() => Action?.Invoke();
    }

    private int _incrementingCounter = 1000;
    private readonly Lock _lock = new();
    private readonly Dictionary<DeviceInfo, MqttConfiguration> _devices = [];

    public IDisposable RegisterDevices(MqttConfiguration mqttConfiguration, params DeviceInfo[] devices)
    {
        lock (_devices)
        {
            foreach (var device in devices)
                _devices[device] = mqttConfiguration;
        }

        return new CallbackDisposable(() =>
        {
            lock (_devices)
            {
                foreach (var device in devices)
                    _devices.Remove(device);
            }
        });
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));

        while (await periodicTimer.WaitForNextTickAsync(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await SendAsync(cancellationToken);
        }
    }

    private async Task SendAsync(CancellationToken cancellationToken = default)
    {
        var keys = _devices.Keys;

        for (var i = keys.Count - 1; i >= 0; i--)
        {
            var device = keys.ElementAt(i);
            var mqttConfiguration = _devices[device];

            var topic = device.Session switch
            {
                AppSession appSession => $"/app/{appSession.User.Id}/{device.SerialNumber}/thing/property/set",
                OpenSession => $"/open/${mqttConfiguration.Username}/${device.SerialNumber}/set",
                _ => throw new NotSupportedException($"Unsupported session type: {device.Session}")
            };

            var payload = $$"""{"params":{},"operateType":"getAllTaskCfg","from":"Android","id":"{{CreateId()}}","lang":"en-us","version":"1.0","moduleSn":"{{device.SerialNumber}}","moduleType":1}""";

            await mqttApi.SendMessageAsync(device, topic, payload, cancellationToken);

            Console.WriteLine($"📡 Sent polling message to {device}");
        }
    }

    private string CreateId()
    {
        using var lockScope = _lock.EnterScope();

        var formattedDateString = DateTime.Now.ToString("ssfff", CultureInfo.InvariantCulture);
        var currentIncrement = _incrementingCounter++;

        if (currentIncrement >= 9999)
        {
            _incrementingCounter = 1001;
            currentIncrement = 1000;
        }

        var generatedIdentifier = formattedDateString + currentIncrement;

        if (generatedIdentifier.Length > 9)
            return generatedIdentifier[^9..];

        return generatedIdentifier;
    }
}