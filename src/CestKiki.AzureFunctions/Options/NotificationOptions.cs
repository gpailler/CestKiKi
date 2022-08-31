namespace CestKiki.AzureFunctions.Options;

public class NotificationOptions
{
    public const string Key = "Notification";

    public string? WebHook { get; set; }

    public string StandUpTimeZone { get; set; } = "Europe/Paris"; // Zone ID from https://nodatime.org/TimeZones

    public TimeSpan StandUpStartTime { get; set; } = TimeSpan.Parse("10:00:00");

    public TimeSpan StandUpEndTime { get; set; } = TimeSpan.Parse("10:10:00");

    public TimeSpan NotificationTime { get; set; } = TimeSpan.Parse("10:15:00");

    public TimeSpan MinimumSharingDuration { get; set; } = TimeSpan.FromMinutes(1);
}
