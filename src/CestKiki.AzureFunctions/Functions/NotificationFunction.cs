using System.Net.Http.Json;

using Azure.Data.Tables;

using CestKiki.AzureFunctions.Dto;
using CestKiki.AzureFunctions.Options;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NodaTime;
using NodaTime.Extensions;

namespace CestKiki.AzureFunctions.Functions;

public class NotificationFunction
{
#if DEBUG
    private const bool RunOnStartup = true;
#else
    private const bool RunOnStartup = false;
#endif

    private static readonly TimeSpan NotificationThreshold = TimeSpan.FromMinutes(10);

    private readonly TableClient _tableClient;
    private readonly IClock _clock;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<NotificationOptions> _options;
    private readonly ILogger<NotificationFunction> _logger;

    public NotificationFunction(
        TableClient tableClient,
        IClock clock,
        IHttpClientFactory httpClientFactory,
        IOptions<NotificationOptions> options,
        ILogger<NotificationFunction> logger)
    {
        _tableClient = tableClient;
        _clock = clock;
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    [Function("Notification")]
    public async Task Run([TimerTrigger("%NotificationCron%", RunOnStartup = RunOnStartup)] TimerInfo timerInfo, FunctionContext context)
    {
        if (!ShouldRun())
        {
            return;
        }

        var standUp = GetStandUpInterval();

        var allEntities = await _tableClient
            .QueryAsync<ZoomHistoryEntity>()
            .ToArrayAsync();
        var matchingEntities = allEntities
            .Where(_ => IsOverlapping(_, standUp))
            .OrderBy(x => x.StartSharing)
            .ToArray();

        foreach (var entity in matchingEntities)
        {
            _logger.LogInformation("{presenter} was a presenter between {start:t} and {end:t}", entity.Username, entity.StartSharing, entity.EndSharing);
        }

        if (matchingEntities.Length == 1)
        {
            var presenter = matchingEntities.Single().Username;
            var response = await _httpClientFactory.CreateClient().PostAsJsonAsync(
                _options.Value.WebHook,
                new {text = $"{presenter} was presenting the stand-up meeting today"});
            response.EnsureSuccessStatusCode();
        }
        else if (matchingEntities.Length > 1)
        {
            var presenters = string.Join(", ", matchingEntities.Select(x => x.Username));
            var response = await _httpClientFactory.CreateClient().PostAsJsonAsync(
                _options.Value.WebHook,
                new {text = $"{presenters} were presenting the stand-up meeting today"});
            response.EnsureSuccessStatusCode();
        }

        foreach (var entity in allEntities)
        {
            await _tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey);
        }
    }

    internal Interval GetStandUpInterval()
    {
        var timeZone = DateTimeZoneProviders.Tzdb[_options.Value.StandUpTimeZone];
        var today = _clock.InZone(timeZone).GetCurrentDate();
        var standUpDateTime = new Interval(
            today.At(LocalTime.FromTimeOnly(TimeOnly.FromTimeSpan(_options.Value.StandUpStartTime))).InZoneStrictly(timeZone).ToInstant(),
            today.At(LocalTime.FromTimeOnly(TimeOnly.FromTimeSpan(_options.Value.StandUpEndTime))).InZoneStrictly(timeZone).ToInstant());

        return standUpDateTime;
    }

    private bool ShouldRun()
    {
        var timeZone = DateTimeZoneProviders.Tzdb[_options.Value.StandUpTimeZone];
        var currentTime = this._clock.InZone(timeZone).GetCurrentTimeOfDay().ToTimeOnly().ToTimeSpan();
        var notificationTime = _options.Value.NotificationTime;
        var diff = currentTime - notificationTime;
        if (currentTime - notificationTime < TimeSpan.Zero)
        {
            diff = diff.Negate();
        }

        if (diff > NotificationThreshold)
        {
            _logger.LogInformation("Current time is '{currentTime}' and notification is scheduled for '{notificationTime}'", currentTime, notificationTime);
            return false;
        }

        return true;
    }

    private bool IsOverlapping(ZoomHistoryEntity entity, Interval interval)
    {
        var sharingInterval = new Interval(
            Instant.FromDateTimeOffset(entity.StartSharing),
            Instant.FromDateTimeOffset(entity.EndSharing.GetValueOrDefault(DateTimeOffset.Now))
        );

        if (sharingInterval.Duration.ToTimeSpan() < _options.Value.MinimumSharingDuration)
        {
            return false;
        }

        return sharingInterval.Start < interval.End && sharingInterval.End > interval.Start;
    }
}
