using Ecoflow.Corebiz.Mqtt.Proto.Common;
using EcoFlow.Mqtt.Api.Extensions;
using EcoFlow.Mqtt.Api.Models;
using EcoFlow.Mqtt.Api.Session;
using Microsoft.Extensions.Hosting;
using MQTTnet;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Nodes;

namespace EcoFlow.Mqtt.Api.Services;

public class InternalMqttApi(InternalHttpApi httpApi) : IHostedService
{
    private static readonly Encoding Encoding = new UTF8Encoding(false, true);
    private readonly ConcurrentDictionary<ISession, MqttState> _states = [];

    public IReadOnlyDictionary<string, JsonNode> Devices
    {
        get
        {
            lock (_states)
            {
                return new OrderedDictionary<string, JsonNode>(_states.Values
                    .SelectMany(state => state.DeviceNodes)
                    .ToDictionary(state => state.Key.SerialNumber, state => state.Value)
                    .OrderBy(state => state.Key));
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

    public async Task SubscribeDeviceAsync(ISession session, MqttConfiguration mqttConfiguration, DeviceInfo deviceInfo, CancellationToken cancellationToken = default)
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
            state.DeviceNodes[deviceInfo] = new JsonObject();

        var subscribeOptionsBuilder = new MqttClientSubscribeOptionsBuilder();

        if (session is AppSession appSession)
            subscribeOptionsBuilder = subscribeOptionsBuilder.WithTopicFilter(filter => filter.WithTopic($"/app/device/property/{deviceInfo.SerialNumber}"));
        else
            subscribeOptionsBuilder = subscribeOptionsBuilder.WithTopicFilter(filter => filter.WithTopic($"/open/{mqttConfiguration.Username}/{deviceInfo.SerialNumber}/quota"));

        // .WithTopicFilter(filter => filter.WithTopic($"/app/{appSession.User.Id}/{deviceInfo.SerialNumber}/thing/property/set"))
        // .WithTopicFilter(filter => filter.WithTopic($"/open/${mqttConfiguration.Username}/${deviceInfo.SerialNumber}/set"))

        var subscribeOptions = subscribeOptionsBuilder.Build();
        await state.Client.SubscribeAsync(subscribeOptions, cancellationToken);
    }

    public async Task SendMessageAsync(DeviceInfo deviceInfo, string topic, string payload, CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .Build();

        foreach (var state in _states.Values)
        {
            if (!state.DeviceNodes.ContainsKey(deviceInfo))
                continue;

            await state.Client.PublishAsync(message, cancellationToken);
        }
    }

    private MqttState GetMqttState(ISession session)
    {
        return _states.GetOrAdd(session, static (_, self) =>
        {
            var mqttClientFactory = new MqttClientFactory();
            var mqttClient = mqttClientFactory.CreateMqttClient();

            mqttClient.ApplicationMessageReceivedAsync += self.OnApplicationMessageReceived;

            return new MqttState(mqttClient, new ConcurrentDictionary<DeviceInfo, JsonNode>(ReferenceEqualityComparer.Instance));
        }, this);
    }

    private async Task OnApplicationMessageReceived(MqttApplicationMessageReceivedEventArgs eventArgs)
    {
        try
        {
            var topic = eventArgs.ApplicationMessage.Topic;
            var parts = topic.Split('/');

            if (parts.Length < 5)
            {
                Console.WriteLine($"⚠️ Invalid topic received: {topic}");
                return;
            }

            var serialNumber = parts[1] switch
            {
                "app" => parts[4], // /app/device/property/{deviceSerialNumber}
                "open" => parts[3], // /open/{mqttConfiguration.Username}/{deviceSerialNumber}/quota
                _ => null
            };

            if (string.IsNullOrWhiteSpace(serialNumber))
            {
                Console.WriteLine($"⚠️ Invalid topic received: {topic}");
                return;
            }

            var deviceInfo = _states.Values.Select(state => state.DeviceNodes.Keys.Single(device => device.SerialNumber == serialNumber)).Single();
            var nodes = new List<JsonNode>(1);

            if (TryParse(eventArgs.ApplicationMessage.Payload, out var payload))
            {
                nodes.Add(JsonNode.Parse(payload)?["params"] ?? throw new InvalidOperationException($"Failed to parse JSON payload: {payload}"));
            }
            else
            {
                var headers = Send_Header_Msg.Parser.ParseFrom(eventArgs.ApplicationMessage.Payload);

                foreach (var header in headers.Decrypted)
                {
                    var message = header.Pdata.AsEcoFlowMessage(header.CmdFunc, header.CmdId);
                    var messageName = message.GetType().Name;

                    if (messageName.EndsWith("PropertyUpload", StringComparison.OrdinalIgnoreCase))
                        nodes.Add(message.ToJson());
                    else if (messageName.Contains("HeartBeatReport", StringComparison.OrdinalIgnoreCase))
                        nodes.Add(message.ToJson());
                    else
                        Console.WriteLine($"⚠️ Unsupported binary message received from {deviceInfo}: {message.ToStringWithTitle()}");
                }
            }

            if (nodes.Count is 0)
                return;

            var updated = false;

            foreach (var state in _states.Values)
            {
                if (!state.DeviceNodes.TryGetValue(deviceInfo, out var deviceNode))
                    continue;

                lock (_states)
                {
                    foreach (var node in nodes)
                    {
                        deviceNode.MergeWith(node);
                        updated = true;
                    }
                }
            }

            Console.WriteLine(updated ? $"🖥  Updated state for {deviceInfo}" : $"⚠️ No devices found for update: {topic} at {DateTime.Now:hh:mm:ss} => {string.Join("\n", nodes)}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"⚠️ Error processing MQTT message: {exception.Message}");
        }

        return;

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
    }

    private record MqttState(IMqttClient Client, ConcurrentDictionary<DeviceInfo, JsonNode> DeviceNodes);
}
