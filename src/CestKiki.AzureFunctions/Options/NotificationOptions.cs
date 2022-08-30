namespace CestKiki.AzureFunctions.Options;

public class NotificationOptions
{
    public const string Key = "Notification";

    public string? WebHook { get; set; }

    public string StandUpTimeZone { get; set; } = "Europe/Paris"; // Zone ID from https://nodatime.org/TimeZones

    public TimeOnly StandUpStartTime { get; set; } = TimeOnly.Parse("10:00:00");

    public TimeOnly StandUpEndTime { get; set; } = TimeOnly.Parse("10:10:00");

    public TimeOnly NotificationTime { get; set; } = TimeOnly.Parse("10:15:00");

    public TimeSpan MinimumSharingDuration { get; set; } = TimeSpan.FromMinutes(1);
}
