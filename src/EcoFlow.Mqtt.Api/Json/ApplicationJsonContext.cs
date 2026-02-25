using EcoFlow.Mqtt.Api.Services;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace EcoFlow.Mqtt.Api.Json;

[JsonSerializable(typeof(InternalHttpApi.EcoFlowAuthenticationPayload))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, JsonNode>))]
internal partial class ApplicationJsonContext : JsonSerializerContext;
