using EcoFlow.Mqtt.Api.Configuration;
using EcoFlow.Mqtt.Api.Configuration.Authentication;
using EcoFlow.Mqtt.Api.Extensions;
using EcoFlow.Mqtt.Api.Json;
using EcoFlow.Mqtt.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Logging.AddFilter(nameof(Microsoft), LogLevel.Warning);
builder.Logging.AddFilter(nameof(System), LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Error);

builder.Services.AddHttpClient();
builder.Services.ConfigureAnonymousHttpClient();
builder.Services.ConfigureEcoFlowEndpoints();
builder.Services.ConfigureEcoFlowAuthentication(errorHandler: () =>
{
    if (OperatingSystem.IsWindows())
    {
        Console.WriteLine("Press any key to exit.");
        _ = Console.ReadKey();
    }

    Environment.Exit(1);
});
builder.Services.AddSingleton<InternalHttpApi>();
builder.Services.AddSingleton<InternalMqttApi>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<InternalMqttApi>());

var app = builder.Build();

var configuration = app.Services.GetRequiredService<IOptions<EcoFlowConfiguration>>();
var authentications = configuration.Value.Authentications;
var authentication = authentications.FirstOrDefault(authentication => authentication is AppAuthentication) ?? authentications.First();

if (authentications.Length > 1)
    Console.WriteLine($"⚠️ Multiple authentication methods configured. Using the {authentication.GetType().Name}.");

var httpApi = app.Services.GetRequiredService<InternalHttpApi>();
var mqttApi = app.Services.GetRequiredService<InternalMqttApi>();

Console.WriteLine("🔑 Authenticating with EcoFlow...");
var session = await httpApi.AuthenticateAsync(authentication);

var devices = await httpApi.GetDevicesAsync(session);
Console.WriteLine($"📱 Device List: {string.Join(", ", devices)}");

var mqttConfiguration = await httpApi.GetMqttConfigurationAsync(session);
Console.WriteLine($"🔌 Subscribing devices to MQTT ({mqttConfiguration.Url}:{mqttConfiguration.Port})...");

foreach (var device in devices)
    await mqttApi.SubscribeDeviceAsync(session, mqttConfiguration, device);

app.MapGet("/", (HttpRequest httpRequest) =>
    httpRequest.Query.ContainsKey("flat")
        ? Results.Text(string.Join('\n', JsonSerializer.SerializeToNode(mqttApi.Devices, ApplicationJsonContext.Default.IReadOnlyDictionaryStringJsonNode).Flatten()))
        : Results.Json(mqttApi.Devices, ApplicationJsonContext.Default.IReadOnlyDictionaryStringJsonNode));

app.MapGet("/{serialNumber}", (HttpRequest httpRequest, string serialNumber) =>
    mqttApi.Devices.TryGetValue(serialNumber, out var device)
    ? httpRequest.Query.ContainsKey("flat")
        ? Results.Text(string.Join('\n', mqttApi.Devices[serialNumber].Flatten()))
        : Results.Json(mqttApi.Devices[serialNumber], ApplicationJsonContext.Default.JsonNode)
    : httpRequest.Query.ContainsKey("flat")
        ? Results.Text(string.Join(',', mqttApi.Devices.Keys), statusCode: StatusCodes.Status404NotFound)
        : Results.Text($"Device not found. Existing serial numbers: {string.Join(',', mqttApi.Devices.Keys)}", statusCode: StatusCodes.Status404NotFound));

app.Lifetime.ApplicationStarted.Register(() =>
{
    foreach (var address in app.Urls)
        Console.WriteLine($"🌐 Listening on: {address}");
});

app.Run();
