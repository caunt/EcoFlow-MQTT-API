using Google.Protobuf;

namespace EcoFlow.Mqtt.Api.Exceptions;

public class MessageMultipleMatchesException(IEnumerable<IMessage> messages, IReadOnlyDictionary<IMessage, int> sizes, int cmdFunc, int cmdId, ByteString byteString) : Exception($"Multiple messages could be parsed from the payload, and the best match is not significantly better than the second best match:\n" +
                                                         $"{string.Join("\n", messages.Take(15).Select(message => $"{sizes[message]} => {message.GetType()}"))}\ncmdFunc: {cmdFunc}, cmdId:{cmdId}\n{Convert.ToHexString(byteString.Span)}")
{
    public IEnumerable<IMessage> Messages => messages;
    public IReadOnlyDictionary<IMessage, int> Sizes => sizes;
    public int CmdFunc => cmdFunc;
    public int CmdId => cmdId;
    public ByteString ByteString => byteString;
}
