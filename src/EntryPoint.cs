using EcoFlow.Mqtt.Api.Configuration;
using EcoFlow.Mqtt.Api.Configuration.Authentication;
using EcoFlow.Mqtt.Api.Extensions;
using EcoFlow.Mqtt.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddFilter(nameof(Microsoft), LogLevel.Warning);
builder.Logging.AddFilter(nameof(System), LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Error);

builder.Services.AddHttpClient();
builder.Services.ConfigureAnonymousHttpClient();
builder.Services.ConfigureEcoFlowEndpoints();
builder.Services.ConfigureEcoFlowAuthentication();
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

app.MapGet("/", () => mqttApi.Devices);
app.Run();
