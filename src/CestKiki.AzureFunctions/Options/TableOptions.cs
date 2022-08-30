namespace CestKiki.AzureFunctions.Options;

public class TableOptions
{
    public const string Key = "Table";

    public string TableName { get; set; } = "CestKikiHistory";

    public string PartitionKey { get; set; } = "ZoomSharing";
}
