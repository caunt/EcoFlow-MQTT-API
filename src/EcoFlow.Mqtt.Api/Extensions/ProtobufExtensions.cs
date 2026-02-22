using Ecoflow.Cbb.Databus.Proto.Common;
using Google.Protobuf;
using Nito.Disposables.Internals;
using System.Reflection;
using System.Text.Json;

namespace EcoFlow.Mqtt.Api.Extensions;

public static class ProtobufExtensions
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = true };
    private static readonly JsonFormatter _jsonFormatter = new(new JsonFormatter.Settings(formatDefaultValues: true));

    extension(IMessage message)
    {
        public int UnknownFieldsSize
        {
            get
            {
                var unknownFieldsProperty = message.GetType().GetField("_unknownFields", BindingFlags.Instance | BindingFlags.NonPublic);

                if (unknownFieldsProperty is null)
                    return 0;

                var unknownFieldsValue = (UnknownFieldSet?)unknownFieldsProperty.GetValue(message);

                if (unknownFieldsValue is null)
                    return 0;

                return unknownFieldsValue.CalculateSize();
            }
        }

        public string ToStringWithTitle()
        {
            return message.GetType() + " " + JsonSerializer.Serialize(JsonDocument.Parse(_jsonFormatter.Format(message)).RootElement, _jsonSerializerOptions);
        }
    }

    extension(MessageParser parser)
    {
        public IMessage? ParseFromSafe(ByteString data)
        {
            try
            {
                var message = parser.ParseFrom(data);

                if (message is null)
                    return null;

                return message;
            }
            catch (InvalidProtocolBufferException)
            {
                return null;
            }
        }
    }

    extension(ByteString byteString)
    {
        public IMessage AsEcoFlowMessage()
        {
            var messages = Send_Header_Msg
                .GetParsers()
                .Select(parser => parser.ParseFromSafe(byteString))
                .WhereNotNull()
                .OrderBy(message => message.UnknownFieldsSize);

            var candidates = messages.Take(2).ToArray();

            if (candidates.Length is 0)
                throw new InvalidOperationException("No message could be parsed from the payload.");

            if (candidates.Length is 1)
                return candidates[0];

            if (candidates[0].UnknownFieldsSize * 2 >= candidates[1].UnknownFieldsSize)
                throw new InvalidOperationException($"Multiple messages could be parsed from the payload, and the best match is not significantly better than the second best match:\n" +
                    $"{string.Join("\n", messages.Take(15).Select(message => $"{message.UnknownFieldsSize} => {message.GetType()}"))}");

            return candidates[0];
        }
    }

    extension<T>(IMessage<T> message) where T : IMessage<T>
    {
        public static MessageParser[] GetParsers()
        {
            return [.. typeof(T).Assembly.GetTypes()
                .Where(type => type is { IsClass: true, IsAbstract: false })
                .Select(type => type.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static))
                .Where(parserProperty => parserProperty is not null)
                .Where(parserProperty =>
                    parserProperty!.PropertyType.IsGenericType &&
                    parserProperty.PropertyType.GetGenericTypeDefinition() == typeof(MessageParser<>))
                .Select(parserProperty => (MessageParser)parserProperty!.GetValue(null)!)];
        }
    }
}
