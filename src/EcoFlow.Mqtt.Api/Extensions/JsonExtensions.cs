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

        public IEnumerable<string> Flatten(string keySeparator = ".", string arrayFormat = "[{0}]")
        {
            foreach (var flattenedLine in Walk(node, string.Empty))
                yield return flattenedLine;

            IEnumerable<string> Walk(JsonNode? currentNode, string currentPath)
            {
                switch (currentNode)
                {
                    case JsonObject jsonObject:
                        foreach (var jsonProperty in jsonObject)
                        {
                            var nextPath = string.IsNullOrEmpty(currentPath)
                                ? jsonProperty.Key
                                : currentPath + keySeparator + jsonProperty.Key;

                            foreach (var flattenedLine in Walk(jsonProperty.Value, nextPath))
                                yield return flattenedLine;
                        }
                        break;

                    case JsonArray jsonArray:
                        var arrayIndex = 0;

                        foreach (var jsonArrayItem in jsonArray)
                        {
                            var nextPath = currentPath + string.Format(arrayFormat, arrayIndex);

                            foreach (var flattenedLine in Walk(jsonArrayItem, nextPath))
                                yield return flattenedLine;

                            arrayIndex++;
                        }
                        break;

                    case JsonValue jsonValue:
                        var valueAsString = jsonValue.ToString() ?? string.Empty;
                        yield return $"{currentPath}={valueAsString}";
                        break;

                    case null:
                        yield return $"{currentPath}=";
                        break;
                }
            }
        }
    }
}
