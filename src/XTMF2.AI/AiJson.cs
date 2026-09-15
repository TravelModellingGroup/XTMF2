using System.Text.Json;
using System.Text.Json.Serialization;

namespace XTMF2.AI;

internal static class AiJson
{
    internal static readonly JsonSerializerOptions Compact = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}