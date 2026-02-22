using Google.Protobuf;

namespace EcoFlow.Mqtt.Api.Extensions;

public static class EcoFlowExtensions
{
    extension(Ecoflow.Cbb.Databus.Proto.Common.Send_Header_Msg headers)
    {
        public IEnumerable<Ecoflow.Cbb.Databus.Proto.Common.Header> Decrypted
        {
            get
            {
                foreach (var header in headers.Msg)
                {
                    if (header.EncType is 1)
                        header.Pdata = Decrypt(header.Pdata, header.Seq);

                    yield return header;
                }
            }
        }
    }

    extension(Ecoflow.Corebiz.Mqtt.Proto.Common.Send_Header_Msg headers)
    {
        public IEnumerable<Ecoflow.Corebiz.Mqtt.Proto.Common.Header> Decrypted
        {
            get
            {
                foreach (var header in headers.Msg)
                {
                    if (header.EncType is 1)
                        header.Pdata = Decrypt(header.Pdata, header.Seq);

                    yield return header;
                }
            }
        }
    }

    extension(Ecoflow.Wn100Module.Pb.WnCommon.Send_Header_Msg headers)
    {
        public IEnumerable<Ecoflow.Wn100Module.Pb.WnCommon.Header> Decrypted
        {
            get
            {
                foreach (var header in headers.Msg)
                {
                    if (header.EncType is 1)
                        header.Pdata = Decrypt(header.Pdata, header.Seq);

                    yield return header;
                }
            }
        }
    }

    private static ByteString Decrypt(ByteString byteString, int sequence)
    {
        var span = byteString.Span;
        var xorSpan = (stackalloc byte[span.Length]);

        for (var index = 0; index < span.Length; index++)
            xorSpan[index] = (byte)(span[index] ^ sequence);

        return ByteString.CopyFrom(xorSpan);
    }
}
