using EcoFlow.Mqtt.Api.Configuration.Authentication;
using System.Security.Cryptography;
using System.Text;

namespace EcoFlow.Mqtt.Api.Session;

public record OpenSession(OpenAuthentication Authentication) : ISession
{
    public void SignRequest(HttpRequestMessage httpRequestMessage)
    {
        var timestampMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var nonce = RandomNumberGenerator.GetInt32(0, 1_000_000 /* 6 digits limit */).ToString("D6");

        var signatureSource = $"accessKey={Authentication.AccessKey}&nonce={nonce}&timestamp={timestampMilliseconds}";
        var signature = HMACSHA256.HashData(Authentication.SecretKeyBytes, Encoding.UTF8.GetBytes(signatureSource));
        var signatureHex = Convert.ToHexString(signature);
        signatureHex = signatureHex.ToLowerInvariant();

        if (!httpRequestMessage.Headers.TryAddWithoutValidation("accessKey", Authentication.AccessKey))
            throw new InvalidOperationException("Failed to add accessKey header.");

        if (!httpRequestMessage.Headers.TryAddWithoutValidation("timestamp", timestampMilliseconds))
            throw new InvalidOperationException("Failed to add timestamp header.");

        if (!httpRequestMessage.Headers.TryAddWithoutValidation("nonce", nonce))
            throw new InvalidOperationException("Failed to add nonce header.");

        if (!httpRequestMessage.Headers.TryAddWithoutValidation("sign", signatureHex))
            throw new InvalidOperationException("Failed to add signature header.");
    }
}
