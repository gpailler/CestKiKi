namespace CestKiki.AzureFunctions.Dto;

public class ZoomHistoryEntity : TableEntityBase
{
    public string? UserId { get; set; }

    public string? Username { get; set; }

    public string? RoomId { get; set; }

    public string? RoomName { get; set; }

    public DateTimeOffset StartSharing { get; set; }

    public DateTimeOffset? EndSharing { get; set; }
}
