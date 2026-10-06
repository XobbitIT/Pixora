using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CanvasForge.Core;

internal static class CanonicalJson
{
    internal static string Serialize(JsonNode? node)=>Normalize(node)?.ToJsonString()??"null";
    private static JsonNode? Normalize(JsonNode? node)
    {
        if(node is JsonObject obj)
        {
            var result=new JsonObject();foreach(var pair in obj.OrderBy(p=>p.Key,StringComparer.Ordinal))result[pair.Key]=Normalize(pair.Value);return result;
        }
        if(node is JsonArray array)return new JsonArray(array.Select(Normalize).ToArray());
        if(node is JsonValue value&&value.GetValueKind()==JsonValueKind.Number
            &&double.TryParse(value.ToJsonString(),NumberStyles.Float,CultureInfo.InvariantCulture,out double number)&&double.IsFinite(number))
            return JsonValue.Create(number==0?0:number);
        return node?.DeepClone();
    }
}
