using Ecoflow.Corebiz.Mqtt.Proto.Common;
using EcoFlow.Mqtt.Api.Extensions;
using EcoFlow.Mqtt.Api.Models;
using EcoFlow.Mqtt.Api.Session;
using Microsoft.Extensions.Hosting;
using MQTTnet;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Nodes;

namespace EcoFlow.Mqtt.Api.Services;

public class InternalMqttApi : IHostedService
{
    private static readonly Encoding Encoding = new UTF8Encoding(false, true);
    private readonly ConcurrentDictionary<ISession, MqttState> _states = [];

    public IReadOnlyDictionary<string, JsonNode> Devices
    {
        get
        {
            lock (_states)
            {
                return _states.Values
                    .SelectMany(state => state.Devices)
                    .ToFrozenDictionary(devices => devices.Key, devices => devices.Value);
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var state in _states.Values)
            await state.Client.DisconnectAsync(cancellationToken: cancellationToken);

        _states.Clear();
    }

    public async Task SubscribeDeviceAsync(ISession session, MqttConfiguration mqttConfiguration, string deviceSerialNumber, CancellationToken cancellationToken = default)
    {
        var state = GetMqttState(session);

        if (!state.Client.IsConnected)
        {
            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(mqttConfiguration.Url, mqttConfiguration.Port)
                .WithCredentials(mqttConfiguration.Username, mqttConfiguration.Password);

            if (session is AppSession appSessionForClientOptions)
                optionsBuilder = optionsBuilder.WithClientId($"ANDROID_{Guid.NewGuid().ToString().ToUpper()}_{appSessionForClientOptions.User.Id}");
            else
                optionsBuilder = optionsBuilder.WithClientId("github.com/caunt/EcoFlow-MQTT-API");

            if (mqttConfiguration.Tls)
                optionsBuilder = optionsBuilder.WithTlsOptions(new MqttClientTlsOptions { UseTls = true });

            var options = optionsBuilder.Build();
            await state.Client.ConnectAsync(options, cancellationToken);
        }

        lock (_states)
            state.Devices[deviceSerialNumber] = new JsonObject();

        var subscribeOptionsBuilder = new MqttClientSubscribeOptionsBuilder();

        if (session is AppSession appSession)
            subscribeOptionsBuilder = subscribeOptionsBuilder.WithTopicFilter(filter => filter.WithTopic($"/app/device/property/{deviceSerialNumber}"));
        else
            subscribeOptionsBuilder = subscribeOptionsBuilder.WithTopicFilter(filter => filter.WithTopic($"/open/{mqttConfiguration.Username}/{deviceSerialNumber}/quota"));

        // .WithTopicFilter(filter => filter.WithTopic($"/app/{appSession.User.Id}/{deviceSerialNumber}/thing/property/set"))
        // .WithTopicFilter(filter => filter.WithTopic($"/open/${mqttConfiguration.Username}/${deviceSerialNumber}/set"))

        var subscribeOptions = subscribeOptionsBuilder.Build();
        await state.Client.SubscribeAsync(subscribeOptions, cancellationToken);
    }

    private MqttState GetMqttState(ISession session)
    {
        return _states.GetOrAdd(session, static (_, self) =>
        {
            var mqttClientFactory = new MqttClientFactory();
            var mqttClient = mqttClientFactory.CreateMqttClient();

            mqttClient.ApplicationMessageReceivedAsync += self.OnApplicationMessageReceived;

            return new MqttState(mqttClient, []);
        }, this);
    }

    private Task OnApplicationMessageReceived(MqttApplicationMessageReceivedEventArgs eventArgs)
    {
        try
        {
            var topic = eventArgs.ApplicationMessage.Topic;
            var parts = topic.Split('/');

            if (parts.Length < 5)
            {
                Console.WriteLine($"⚠️ Invalid topic received: {topic}");
                return Task.CompletedTask;
            }

            var serialNumber = parts[1] switch
            {
                "app" => parts[4], // /app/device/property/{deviceSerialNumber}
                "open" => parts[3], // /open/{mqttConfiguration.Username}/{deviceSerialNumber}/quota
                _ => null
            };

            if (serialNumber is null)
            {
                Console.WriteLine($"⚠️ Invalid topic received: {topic}");
                return Task.CompletedTask;
            }

            if (!TryParse(eventArgs.ApplicationMessage.Payload, out var payload))
            {
                Console.WriteLine($"⚠️ Binary payload received for {serialNumber}");
                var headers = Send_Header_Msg.Parser.ParseFrom(eventArgs.ApplicationMessage.Payload);

                foreach (var header in headers.Decrypted)
                {
                    var message = header.Pdata.AsEcoFlowMessage();
                    Console.WriteLine(message.ToStringWithTitle());
                }

                return Task.CompletedTask;
            }

            var node = JsonNode.Parse(payload);

            if (node is null)
                return Task.CompletedTask;

            var updated = false;

            foreach (var state in _states.Values)
            {
                if (!state.Devices.TryGetValue(serialNumber, out var device))
                    continue;

                lock (_states)
                    MergeJsonNodes(device, node);

                updated = true;
            }

            Console.WriteLine(updated ? $"🖥  Updated state for {serialNumber}" : $"⚠️ No devices found for update: {topic} at {DateTime.Now:hh:mm:ss} => {node}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"⚠️ Error processing MQTT message: {exception.Message}");
        }

        return Task.CompletedTask;

        static bool TryParse(ReadOnlySequence<byte> bytes, [MaybeNullWhen(false)] out string value)
        {
            try
            {
                value = Encoding.GetString(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                value = null;
                return false;
            }
        }

        static void MergeJsonNodes(JsonNode previousNode, JsonNode nextNode)
        {
            if (previousNode is not JsonObject previousObject || nextNode is not JsonObject nextObject)
                return;

            foreach (var nextProperty in nextObject)
            {
                var previousProperty = previousObject[nextProperty.Key];

                if (previousProperty is JsonObject && nextProperty.Value is JsonObject)
                {
                    MergeJsonNodes(previousProperty, nextProperty.Value);
                }
                else
                {
                    var clonedNode = nextProperty.Value?.DeepClone();
                    clonedNode.Sort();

                    previousObject[nextProperty.Key] = clonedNode;
                    previousObject.Sort();
                }
            }
        }
    }

    private record MqttState(IMqttClient Client, ConcurrentDictionary<string, JsonNode> Devices);
}
