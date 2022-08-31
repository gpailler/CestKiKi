using System.Text.Json.Serialization;

using CestKiki.AzureFunctions.Converters;

namespace CestKiki.AzureFunctions.Dto;

/// <summary>
/// Mapping for the following JSON bodies:
/// https://marketplace.zoom.us/docs/api-reference/zoom-api/events/#operation/meeting.sharing_started
/// https://marketplace.zoom.us/docs/api-reference/zoom-api/events/#operation/meeting.sharing_ended
/// https://marketplace.zoom.us/docs/api-reference/zoom-api/events/#operation/meeting.participant_left
/// </summary>
internal class ZoomHookPayload
{
    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("payload")]
    public ZoomPayload? Payload { get; set; }

    [JsonPropertyName("event_ts")]
    [JsonConverter(typeof(UnixToNullableDateTimeOffsetConverter))]
    public DateTimeOffset? Timestamp { get; set; }

    public class ZoomPayload
    {
        [JsonPropertyName("object")]
        public ZoomObject? Object { get; set; }

        public class ZoomObject
        {
            [JsonPropertyName("participant")]
            public ZoomParticipant? Participant { get; set; }

            [JsonPropertyName("id")]
            public string? RoomId { get; set; }

            [JsonPropertyName("topic")]
            public string? RoomTopic { get; set; }

            public class ZoomParticipant
            {
                [JsonPropertyName("user_id")]
                public string? UserId { get; set; }

                [JsonPropertyName("user_name")]
                public string? Username { get; set; }
            }
        }
    }
}
