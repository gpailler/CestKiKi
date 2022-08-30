using System.Net;
using System.Net.Http.Headers;

using Azure;
using Azure.Data.Tables;

using CestKiki.AzureFunctions.Dto;
using CestKiki.AzureFunctions.Functions;
using CestKiki.AzureFunctions.Helpers;
using CestKiki.AzureFunctions.Options;

using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

namespace CestKiki.AzureFunctions.Tests.Functions;

public class ZoomWebHookFunctionTests
{
    private readonly Mock<TableClient> _tableClientMock;
    private readonly Mock<IZoomSignatureHelper> _zoomSignatureHelperMock;
    private readonly Mock<ILogger<ZoomWebHookFunction>> _loggerMock;
    private readonly ZoomWebHookFunction _sut;

    public ZoomWebHookFunctionTests()
    {
        _tableClientMock = new Mock<TableClient>();
        _zoomSignatureHelperMock = new Mock<IZoomSignatureHelper>();
        _zoomSignatureHelperMock
            .Setup(_ => _.ValidateSignature(It.IsAny<HttpHeaders>(), It.IsAny<string>()))
            .Returns(true);
        _loggerMock = new Mock<ILogger<ZoomWebHookFunction>>();

        _sut = new ZoomWebHookFunction(
            _tableClientMock.Object,
            Mock.Of<IOptions<TableOptions>>(_ => _.Value == new TableOptions()),
            Mock.Of<IOptions<ZoomOptions>>(_ => _.Value.MonitoredRoom == "987654321"),
            _zoomSignatureHelperMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Run_InvalidSignature_Unauthorized()
    {
        // Arrange
        _zoomSignatureHelperMock
            .Setup(_ => _.ValidateSignature(It.IsAny<HttpHeaders>(), It.IsAny<string>()))
            .Returns(false);
        var requestMock = RequestHelper.CreateMock(string.Empty, new HttpHeadersCollection(new[] {new KeyValuePair<string, string>("key", "value")}));

        // Act
        var response = await _sut.Run(requestMock.Object, requestMock.Object.FunctionContext);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        _loggerMock.VerifyLog(LogLevel.Error, "Invalid message signature. Headers: 'key: value\r\n'");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ invalid }")]
    public async Task Run_InvalidData_BadRequest(string body)
    {
        // Arrange
        var requestMock = RequestHelper.CreateMock(body);

        // Act
        var response = await _sut.Run(requestMock.Object, requestMock.Object.FunctionContext);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _loggerMock.VerifyLog(LogLevel.Error, "Invalid Json payload");
    }

    [Theory]
    [InlineData(@"{""event"":""foo""}", "Zoom event 'foo' is not supported")]
    [InlineData(@"{""event"":""invalid"",""payload"":{""object"":{""participant"":{""user_id"":""12345678"",""user_name"":""Firstname Lastname""},""id"":""987654321"",""topic"":""meeting topic""}},""event_ts"":1659893915330}", "Zoom event 'invalid' is not supported")]
    [InlineData(@"{""event"":""meeting.sharing_started"",""payload"":{""object"":{""participant"":{""user_name"":""Firstname Lastname""},""id"":""987654321"",""topic"":""meeting topic""}},""event_ts"":1659893915330}", "UserId is null")]
    [InlineData(@"{""event"":""meeting.sharing_started"",""payload"":{""object"":{""participant"":{""user_id"":""12345678""},""id"":""987654321"",""topic"":""meeting topic""}},""event_ts"":1659893915330}", "Username is null")]
    [InlineData(@"{""event"":""meeting.sharing_started"",""payload"":{""object"":{""participant"":{""user_id"":""12345678"",""user_name"":""Firstname Lastname""},""topic"":""meeting topic""}},""event_ts"":1659893915330}", "RoomId is null")]
    [InlineData(@"{""event"":""meeting.sharing_started"",""payload"":{""object"":{""participant"":{""user_id"":""12345678"",""user_name"":""Firstname Lastname""},""id"":""987654321""}},""event_ts"":1659893915330}", "RoomTopic is null")]
    [InlineData(@"{""event"":""meeting.sharing_started"",""payload"":{""object"":{""participant"":{""user_id"":""12345678"",""user_name"":""Firstname Lastname""},""id"":""987654321"",""topic"":""meeting topic""}}}", "Timestamp is null")]
    public async Task Run_MissingJsonContent_BadRequest(string body, string expectedError)
    {
        // Arrange
        var requestMock = RequestHelper.CreateMock(body);

        // Act
        var response = await _sut.Run(requestMock.Object, requestMock.Object.FunctionContext);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _loggerMock.VerifyLog<ZoomWebHookFunction, InvalidOperationException>(LogLevel.Error, "Error storing Zoom event", _ => _.Message == expectedError);
    }

    [Theory]
    [InlineData(@"{""event"":""meeting.sharing_started"",""payload"":{""object"":{""participant"":{""user_id"":""12345678"",""user_name"":""Firstname Lastname""},""id"":""999999999"",""topic"":""meeting topic""}},""event_ts"":1659893915330}")]
    [InlineData(@"{""event"":""meeting.sharing_ended"",""payload"":{""object"":{""participant"":{""user_id"":""12345678"",""user_name"":""Firstname Lastname""},""id"":""999999999"",""topic"":""meeting topic""}},""event_ts"":1659893915330}")]
    [InlineData(@"{""event"":""meeting.participant_left"",""payload"":{""object"":{""participant"":{""user_id"":""12345678"",""user_name"":""Firstname Lastname""},""id"":""999999999"",""topic"":""meeting topic""}},""event_ts"":1659893915330}")]
    public async Task Run_NonMonitoredRoom_Ok(string body)
    {
        // Arrange
        var requestMock = RequestHelper.CreateMock(body);

        // Act
        var response = await _sut.Run(requestMock.Object, requestMock.Object.FunctionContext);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _loggerMock.VerifyLog(LogLevel.Debug, "RoomId '999999999' is not monitored");
    }

    [Theory]
    [InlineData(@"{""event"":""meeting.sharing_started"",""payload"":{""object"":{""participant"":{""user_id"":""12345678"",""user_name"":""Firstname Lastname""},""id"":""987654321"",""topic"":""meeting topic""}},""event_ts"":1659893915330}")]
    public async Task Run_SharingStartedAndNoExistingSharing_Ok(string body)
    {
        // Arrange
        ConfigureTableClientMock("12345678", "987654321", null);
        var requestMock = RequestHelper.CreateMock(body);

        // Act
        var response = await _sut.Run(requestMock.Object, requestMock.Object.FunctionContext);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var expectedEntity = new ZoomHistoryEntity
        {
            RoomId = "987654321",
            UserId = "12345678",
            Username = "Firstname Lastname",
            RoomName = "meeting topic",
            StartSharing = DateTimeOffset.FromUnixTimeMilliseconds(1659893915330)
        };
        _tableClientMock.Verify();
        _tableClientMock.Verify(_ => _.AddEntityAsync(It.Is(expectedEntity, new ZoomHistoryEntityComparer()), default));
        _tableClientMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(@"{""event"":""meeting.sharing_started"",""payload"":{""object"":{""participant"":{""user_id"":""12345678"",""user_name"":""Firstname Lastname""},""id"":""987654321"",""topic"":""meeting topic""}},""event_ts"":1659893915330}")]
    public async Task Run_SharingStartedAndExistingSharing_BadRequest(string body)
    {
        // Arrange
        ConfigureTableClientMock("12345678", "987654321", new ZoomHistoryEntity());
        var requestMock = RequestHelper.CreateMock(body);

        // Act
        var response = await _sut.Run(requestMock.Object, requestMock.Object.FunctionContext);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _tableClientMock.Verify();
        _tableClientMock.VerifyNoOtherCalls();
        _loggerMock.VerifyLog<ZoomWebHookFunction, InvalidOperationException>(LogLevel.Error, "Error storing Zoom event", _ => _.Message == "User '12345678' already has a sharing started event on room '987654321'");
    }

    [Theory]
    [InlineData(@"{""event"":""meeting.sharing_ended"",""payload"":{""object"":{""participant"":{""user_id"":""12345678"",""user_name"":""Firstname Lastname""},""id"":""987654321"",""topic"":""meeting topic""}},""event_ts"":1659893915331}")]
    [InlineData(@"{""event"":""meeting.participant_left"",""payload"":{""object"":{""participant"":{""user_id"":""12345678"",""user_name"":""Firstname Lastname""},""id"":""987654321"",""topic"":""meeting topic""}},""event_ts"":1659893915331}")]
    public async Task Run_SharingEndedAndExistingSharing_Ok(string body)
    {
        // Arrange
        var requestMock = RequestHelper.CreateMock(body);
        var existingEntity = new ZoomHistoryEntity
        {
            ETag = new ETag("foo"),
            RoomId = "987654321",
            UserId = "12345678",
            Username = "Firstname Lastname",
            RoomName = "meeting topic",
            StartSharing = DateTimeOffset.FromUnixTimeMilliseconds(1659893915330)
        };
        ConfigureTableClientMock("12345678", "987654321", existingEntity);

        // Act
        var response = await _sut.Run(requestMock.Object, requestMock.Object.FunctionContext);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(existingEntity.EndSharing, DateTimeOffset.FromUnixTimeMilliseconds(1659893915331));
        _tableClientMock.Verify();
        _tableClientMock.Verify(_ => _.UpdateEntityAsync(existingEntity, existingEntity.ETag, TableUpdateMode.Replace, default));
        _tableClientMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(@"{""event"":""meeting.sharing_ended"",""payload"":{""object"":{""participant"":{""user_id"":""12345678"",""user_name"":""Firstname Lastname""},""id"":""987654321"",""topic"":""meeting topic""}},""event_ts"":1659893915331}")]
    [InlineData(@"{""event"":""meeting.participant_left"",""payload"":{""object"":{""participant"":{""user_id"":""12345678"",""user_name"":""Firstname Lastname""},""id"":""987654321"",""topic"":""meeting topic""}},""event_ts"":1659893915331}")]
    public async Task Run_SharingEndedAndNoExistingSharing_Ok(string body)
    {
        // Arrange
        var requestMock = RequestHelper.CreateMock(body);
        ConfigureTableClientMock("12345678", "987654321", null);

        // Act
        var response = await _sut.Run(requestMock.Object, requestMock.Object.FunctionContext);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _tableClientMock.Verify();
        _tableClientMock.VerifyNoOtherCalls();
        _loggerMock.VerifyLog(LogLevel.Error, "User '12345678' has no sharing or multiple sharings active on room '987654321'");
    }

    private void ConfigureTableClientMock(string userId, string roomId, ZoomHistoryEntity? entity)
    {
        var asyncPageable = entity == null
            ? AsyncPageable<ZoomHistoryEntity>.FromPages(Array.Empty<Page<ZoomHistoryEntity>>())
            : AsyncPageable<ZoomHistoryEntity>.FromPages(new[] {Page<ZoomHistoryEntity>.FromValues(new[] {entity}, null, Mock.Of<Response>())});
        _tableClientMock
            .Setup(_ => _.QueryAsync<ZoomHistoryEntity>(filter => filter.UserId == userId && filter.RoomId == roomId, null, null, default))
            .Returns(asyncPageable)
            .Verifiable();
    }

    private class ZoomHistoryEntityComparer : IEqualityComparer<ZoomHistoryEntity>
    {
        public bool Equals(ZoomHistoryEntity? x, ZoomHistoryEntity? y)
        {
            if (x == null && y == null)
            {
                return true;
            }

            if (x == null || y == null)
            {
                return false;
            }

            return x.RoomId == y.RoomId
                   && x.UserId == y.UserId
                   && x.Username == y.Username
                   && x.RoomName == y.RoomName
                   && x.StartSharing == y.StartSharing;
        }

        public int GetHashCode(ZoomHistoryEntity obj)
        {
            return HashCode.Combine(obj.RoomId, obj.UserId, obj.Username, obj.RoomName, obj.StartSharing);
        }
    }
}
