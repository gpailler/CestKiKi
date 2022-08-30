namespace CestKiki.AzureFunctions.Options;

public class ZoomOptions
{
    public const string Key = "Zoom";

    public string? WebHookSecret { get; set; }

    public string? MonitoredRoom { get; set; }
}
