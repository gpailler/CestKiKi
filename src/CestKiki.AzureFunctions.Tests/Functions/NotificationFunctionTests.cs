using System.Net;

using Azure;
using Azure.Data.Tables;

using CestKiki.AzureFunctions.Dto;
using CestKiki.AzureFunctions.Functions;
using CestKiki.AzureFunctions.Options;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;
using Moq.Contrib.HttpClient;

using NodaTime;
using NodaTime.Extensions;
using NodaTime.Text;

namespace CestKiki.AzureFunctions.Tests.Functions;

public class NotificationFunctionTests
{
    private readonly Mock<TableClient> _tableClientMock;
    private readonly Mock<IClock> _clockMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly NotificationOptions _notificationOptions;
    private readonly Mock<ILogger<NotificationFunction>> _loggerMock;
    private readonly NotificationFunction _sut;

    public NotificationFunctionTests()
    {
        _tableClientMock = new Mock<TableClient>();
        _clockMock = new Mock<IClock>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        var factory = _httpMessageHandlerMock.CreateClientFactory();

        var notificationOptionsMock = new Mock<IOptions<NotificationOptions>>();
        _loggerMock = new Mock<ILogger<NotificationFunction>>();

        _notificationOptions = new NotificationOptions();
        notificationOptionsMock.Setup(_ => _.Value).Returns(_notificationOptions);

        _sut = new NotificationFunction(
            _tableClientMock.Object,
            _clockMock.Object,
            factory,
            notificationOptionsMock.Object,
            _loggerMock.Object);
    }

    [Theory]
    [InlineData("2022-08-29T02:59:00", "Europe/Paris", "2022-08-29T08:10:00", "2022-08-29T08:20:00")]
    [InlineData("2022-12-29T02:59:00", "Europe/Paris", "2022-12-29T09:10:00", "2022-12-29T09:20:00")]
    [InlineData("2022-08-29T02:59:00", "Asia/Kuala_Lumpur", "2022-08-29T02:10:00", "2022-08-29T02:20:00")]
    [InlineData("2022-12-29T02:59:00", "Asia/Kuala_Lumpur", "2022-12-29T02:10:00", "2022-12-29T02:20:00")]
    public void GetStandUpInterval_Succeeds(string currentDatetimeUtc, string timeZone, string expectedLocalStart, string expectedLocalEnd)
    {
        // Arrange
        _clockMock.Setup(_ => _.GetCurrentInstant()).Returns(GetInstant(currentDatetimeUtc));
        _notificationOptions.StandUpTimeZone = timeZone;
        _notificationOptions.StandUpStartTime = TimeOnly.Parse("10:10:00");
        _notificationOptions.StandUpEndTime = TimeOnly.Parse("10:20:00");

        // Act
        var interval = _sut.GetStandUpInterval();

        // Assert
        Assert.Equal(interval.Duration, Duration.FromMinutes(10));
        Assert.Equal(interval.Start, LocalDateTimePattern.GeneralIso.Parse(expectedLocalStart).GetValueOrThrow().InUtc().ToInstant());
        Assert.Equal(interval.End, LocalDateTimePattern.GeneralIso.Parse(expectedLocalEnd).GetValueOrThrow().InUtc().ToInstant());
    }

    [Theory]
    [InlineData("2022-08-29T06:30:00", "08:30:00", true)]
    [InlineData("2022-08-29T06:30:00", "08:00:00", false)]
    [InlineData("2022-12-29T07:30:00", "08:30:00", true)]
    [InlineData("2022-12-29T07:30:00", "08:00:00", false)]
    [InlineData("2022-08-29T01:00:00", "03:05:00", true)]
    [InlineData("2022-08-29T01:00:00", "03:10:00", true)]
    [InlineData("2022-08-29T01:00:00", "03:10:01", false)]
    [InlineData("2022-08-29T01:00:00", "02:55:00", true)]
    [InlineData("2022-08-29T01:00:00", "02:50:00", true)]
    [InlineData("2022-08-29T01:00:00", "02:49:59", false)]
    public async Task Run_OnlyAtNotificationTimeSucceeds(string currentDatetimeUtc, string notificationTime, bool expectedRun)
    {
        // Arrange
        _clockMock.Setup(_ => _.GetCurrentInstant()).Returns(GetInstant(currentDatetimeUtc));
        _notificationOptions.StandUpTimeZone = "Europe/Paris";
        _notificationOptions.NotificationTime = TimeOnly.Parse(notificationTime);
        _tableClientMock
            .Setup(_ => _.QueryAsync<ZoomHistoryEntity>((string)null!, null, null, default))
            .Returns(AsyncPageable<ZoomHistoryEntity>.FromPages(Array.Empty<Page<ZoomHistoryEntity>>()));

        // Act
        await _sut.Run(new TimerInfo(), Mock.Of<FunctionContext>());

        // Assert
        if (expectedRun)
        {
            _tableClientMock.Verify(_ => _.QueryAsync<ZoomHistoryEntity>((string)null!, null, null, default));
        }
        else
        {
            var timeZone = DateTimeZoneProviders.Tzdb[_notificationOptions.StandUpTimeZone];
            var expectedCurrentTime = this._clockMock.Object.InZone(timeZone).GetCurrentTimeOfDay().ToTimeOnly().ToTimeSpan();
            var expectedNotificationTime = _notificationOptions.NotificationTime.ToTimeSpan();
            _loggerMock.VerifyLog(LogLevel.Information, $"Current time is '{expectedCurrentTime}' and notification is scheduled for '{expectedNotificationTime}'");
            _tableClientMock.VerifyNoOtherCalls();
        }
    }

    [Theory]
    [MemberData(nameof(GetTestCase))]
    public async Task Run_Succeeds(ZoomHistoryEntity[]? entities, TimeOnly startTime, TimeOnly endTime, TimeSpan minimumSharingDuration, string? notification)
    {
        // Arrange
        _clockMock.Setup(_ => _.GetCurrentInstant()).Returns(GetInstant("2022-08-29T02:59:00"));
        _notificationOptions.StandUpTimeZone = "Europe/Paris";
        _notificationOptions.StandUpStartTime = startTime;
        _notificationOptions.StandUpEndTime = endTime;
        _notificationOptions.WebHook = "https://example.com/webhook";
        _notificationOptions.MinimumSharingDuration = minimumSharingDuration;
        _notificationOptions.NotificationTime = TimeOnly.Parse("05:00:00");

        var pages = entities == null
            ? AsyncPageable<ZoomHistoryEntity>.FromPages(Array.Empty<Page<ZoomHistoryEntity>>())
            : AsyncPageable<ZoomHistoryEntity>.FromPages(new[] {Page<ZoomHistoryEntity>.FromValues(entities, null, Mock.Of<Response>())});
        _tableClientMock
            .Setup(_ => _.QueryAsync<ZoomHistoryEntity>((string)null!, null, null, default))
            .Returns(pages)
            .Verifiable();

        string? requestBody = null;
        _httpMessageHandlerMock
            .SetupRequest(HttpMethod.Post, new Uri(_notificationOptions.WebHook))
            .ReturnsResponse(HttpStatusCode.OK)
            .Callback((HttpRequestMessage request, CancellationToken _) => requestBody = request.Content!.ReadAsStringAsync(_).Result)
            .Verifiable();

        // Act
        await _sut.Run(new TimerInfo(), Mock.Of<FunctionContext>());

        // Assert
        _tableClientMock.Verify();
        if (entities != null)
        {
            foreach (var entity in entities)
            {
                _tableClientMock.Verify(_ => _.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, default, default));
            }
        }

        _tableClientMock.VerifyNoOtherCalls();
        if (notification != null)
        {
            _httpMessageHandlerMock.Verify();
            Assert.Equal(notification, requestBody);
        }

        _httpMessageHandlerMock.VerifyNoOtherCalls();
    }

    public static IEnumerable<object?[]> GetTestCase()
    {
        // No sharing
        yield return new object?[]
        {
            null,
            TimeOnly.Parse("10:10:00"),
            TimeOnly.Parse("10:20:00"),
            TimeSpan.FromMinutes(1),
            null
        };

        var entity1 = new ZoomHistoryEntity
        {
            PartitionKey = "partitionKey",
            RowKey = "rowKey1",
            Username = "User1",
            StartSharing = GetInstant("2022-08-29T08:12:00").ToDateTimeOffset(),
            EndSharing = GetInstant("2022-08-29T08:14:00").ToDateTimeOffset()
        };

        var entity2 = new ZoomHistoryEntity
        {
            PartitionKey = "partitionKey",
            RowKey = "rowKey2",
            Username = "User2",
            StartSharing = GetInstant("2022-08-29T08:15:00").ToDateTimeOffset(),
            EndSharing = GetInstant("2022-08-29T08:18:00").ToDateTimeOffset()
        };

        // Single sharing and single match
        yield return new object?[]
        {
            new object[] { entity1 },
            TimeOnly.Parse("10:10:00"),
            TimeOnly.Parse("10:20:00"),
            TimeSpan.FromMinutes(1),
            "{\"text\":\"User1 was presenting the stand-up meeting today\"}"
        };

        // Single sharing and no match
        yield return new object?[]
        {
            new object[] { entity1 },
            TimeOnly.Parse("10:15:00"),
            TimeOnly.Parse("10:20:00"),
            TimeSpan.FromMinutes(1),
            null
        };

        // Multiple sharing and multiple matches
        yield return new object?[]
        {
            new[] { entity1, entity2 },
            TimeOnly.Parse("10:10:00"),
            TimeOnly.Parse("10:20:00"),
            TimeSpan.FromMinutes(1),
            "{\"text\":\"User1, User2 were presenting the stand-up meeting today\"}"
        };

        // Multiple sharing and multiple matches (multiple partial overlaps)
        yield return new object?[]
        {
            new[] { entity1, entity2 },
            TimeOnly.Parse("10:12:30"),
            TimeOnly.Parse("10:17:00"),
            TimeSpan.FromMinutes(1),
            "{\"text\":\"User1, User2 were presenting the stand-up meeting today\"}"
        };

        // Multiple sharings and multiple matches (global overlap)
        yield return new object?[]
        {
            new[] { entity1, entity2 },
            TimeOnly.Parse("10:00:00"),
            TimeOnly.Parse("10:30:00"),
            TimeSpan.FromMinutes(1),
            "{\"text\":\"User1, User2 were presenting the stand-up meeting today\"}"
        };

        // Multiple sharings and single match (partial overlap)
        yield return new object?[]
        {
            new[] { entity1, entity2 },
            TimeOnly.Parse("10:00:00"),
            TimeOnly.Parse("10:13:30"),
            TimeSpan.FromMinutes(1),
            "{\"text\":\"User1 was presenting the stand-up meeting today\"}"
        };
        yield return new object?[]
        {
            new[] { entity1, entity2 },
            TimeOnly.Parse("10:16:30"),
            TimeOnly.Parse("10:30:00"),
            TimeSpan.FromMinutes(1),
            "{\"text\":\"User2 was presenting the stand-up meeting today\"}"
        };

        // Short sharing skipped
        yield return new object?[]
        {
            new[] { entity1, entity2 },
            TimeOnly.Parse("10:10:00"),
            TimeOnly.Parse("10:20:00"),
            TimeSpan.FromMinutes(2.5),
            "{\"text\":\"User2 was presenting the stand-up meeting today\"}"
        };
    }

    private static Instant GetInstant(string datetimeUtc)
    {
        return LocalDateTimePattern.GeneralIso.Parse(datetimeUtc).GetValueOrThrow().InUtc().ToInstant();
    }
}
