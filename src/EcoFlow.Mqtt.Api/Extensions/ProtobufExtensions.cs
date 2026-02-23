using Google.Protobuf;
using Nito.Disposables.Internals;
using ProtobufUtilities;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EcoFlow.Mqtt.Api.Extensions;

public static class ProtobufExtensions
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = true };
    private static readonly JsonFormatter _jsonFormatter = new(new JsonFormatter.Settings(formatDefaultValues: true));

    extension(IMessage message)
    {
        public JsonNode ToJson()
        {
            return JsonNode.Parse(_jsonFormatter.Format(message)) ?? throw new InvalidOperationException("Failed to parse JSON from the message.");
        }

        public string ToStringWithTitle()
        {
            return message.GetType() + " " + JsonSerializer.Serialize(message.ToJson(), _jsonSerializerOptions);
        }
    }

    extension(ByteString byteString)
    {
        public IMessage AsEcoFlowMessage()
        {
            var sizes = new Dictionary<IMessage, int>();
            var messages = ProtobufMessages.Parsers
                .Select(parser =>
                {
                    IMessage? message = null;

                    try
                    {
                        message = parser.ParseFrom(byteString);
                    }
                    catch (InvalidProtocolBufferException)
                    {
                        return null;
                    }

                    if (message is not null)
                        sizes[message] = message.UnknownFields.CalculateSize();

                    return message;
                })
                .WhereNotNull()
                .OrderBy(message => sizes[message]);

            var candidates = messages.Take(2).ToArray();

            if (candidates.Length is 0)
                throw new InvalidOperationException("No message could be parsed from the payload.");

            if (candidates.Length is 1)
                return candidates[0];

            if (sizes[candidates[0]] * 2 >= sizes[candidates[1]])
                throw new InvalidOperationException($"Multiple messages could be parsed from the payload, and the best match is not significantly better than the second best match:\n" +
                    $"{string.Join("\n", messages.Take(15).Select(message => $"{sizes[message]} => {message.GetType()}"))}");

            return candidates[0];
        }
    }
}
