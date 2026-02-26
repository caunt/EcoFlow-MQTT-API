using EcoFlow.Mqtt.Api.Session;
using System.Text;

namespace EcoFlow.Mqtt.Api.Models;

public record DeviceInfo(ISession Session, string SerialNumber, string Title)
{
    private string? _cachedToString;

    public override string ToString()
    {
        lock (this)
            return _cachedToString ??= BuildString();

        string BuildString()
        {
            var stringBuilder = new StringBuilder();

            if (!Title.Contains("EcoFlow", StringComparison.OrdinalIgnoreCase))
                stringBuilder.Append("EcoFlow ");

            stringBuilder.Append(Title);
            stringBuilder.Append(" - ");
            stringBuilder.Append(SerialNumber, SerialNumber.Length - 4, 4);

            return stringBuilder.ToString();
        }
    }
}
