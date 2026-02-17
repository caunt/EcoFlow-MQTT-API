using System.Text.Json.Nodes;

namespace EcoFlow.Mqtt.Api.Extensions;

public static class JsonExtensions
{
    extension(JsonNode? node)
    {
        public void Sort()
        {
            switch (node)
            {
                case JsonObject jsonObject:
                    {
                        var properties = jsonObject
                            .Select(property => property.Key)
                            .ToArray();

                        foreach (var propertyName in properties)
                            Sort(jsonObject[propertyName]);

                        var sortedProperties = jsonObject
                            .OrderBy(property => property.Key, StringComparer.Ordinal)
                            .ToArray();

                        jsonObject.Clear();

                        foreach (var sortedProperty in sortedProperties)
                            jsonObject[sortedProperty.Key] = sortedProperty.Value;

                        break;
                    }
                case JsonArray jsonArray:
                    {
                        foreach (var arrayItem in jsonArray)
                            Sort(arrayItem);

                        break;
                    }
            }
        }
    }
}
