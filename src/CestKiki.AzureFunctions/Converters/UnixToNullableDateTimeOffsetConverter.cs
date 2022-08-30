using System.Text.Json;
using System.Text.Json.Serialization;

namespace CestKiki.AzureFunctions.Converters;

internal class UnixToNullableDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override bool HandleNull => true;

    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TryGetInt64(out var time))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(time);
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options) => throw new NotSupportedException();
}
