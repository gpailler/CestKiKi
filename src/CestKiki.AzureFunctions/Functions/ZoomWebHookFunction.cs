using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Azure.Data.Tables;

using CestKiki.AzureFunctions.Dto;
using CestKiki.AzureFunctions.Helpers;
using CestKiki.AzureFunctions.Options;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CestKiki.AzureFunctions.Functions
{
    public class ZoomWebHookFunction
    {
        private readonly TableClient _tableClient;
        private readonly TableOptions _tableOptions;
        private readonly ZoomOptions _zoomOptions;
        private readonly IZoomSignatureHelper _zoomSignatureHelper;
        private readonly ILogger<ZoomWebHookFunction> _logger;

        public ZoomWebHookFunction(
            TableClient tableClient,
            IOptions<TableOptions> tableOptions,
            IOptions<ZoomOptions> zoomOptions,
            IZoomSignatureHelper zoomSignatureHelper,
            ILogger<ZoomWebHookFunction> logger)
        {
            _tableClient = tableClient;
            _tableOptions = tableOptions.Value;
            _zoomOptions = zoomOptions.Value;
            _zoomSignatureHelper = zoomSignatureHelper;
            _logger = logger;
        }

        [Function("ZoomWebHook")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData request, FunctionContext executionContext)
        {
            using var streamReader = new StreamReader(request.Body);
            var body = await streamReader.ReadToEndAsync();

            if (!ValidateSignature(request.Headers, body))
            {
                return request.CreateResponse(HttpStatusCode.Unauthorized);
            }

            ZoomHookPayload? zoomHookPayload = DeserializeZoomHookPayload(body);
            if (zoomHookPayload == null)
            {
                return request.CreateResponse(HttpStatusCode.BadRequest);
            }

            try
            {
                switch (zoomHookPayload.Event)
                {
                    case "meeting.sharing_started":
                        await StoreSharingStartedEventAsync(zoomHookPayload);
                        return request.CreateResponse(HttpStatusCode.OK);

                    case "meeting.sharing_ended":
                    case "meeting.participant_left":
                        await StoreSharingEndedEventAsync(zoomHookPayload);
                        return request.CreateResponse(HttpStatusCode.OK);

                    default:
                        throw new InvalidOperationException($"Zoom event '{zoomHookPayload.Event}' is not supported");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error storing Zoom event");
            }

            return request.CreateResponse(HttpStatusCode.BadRequest);
        }

        private ZoomHookPayload? DeserializeZoomHookPayload(string body)
        {
            ZoomHookPayload? zoomHookPayload = null;
            try
            {
                zoomHookPayload = JsonSerializer.Deserialize<ZoomHookPayload>(body);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Invalid Json payload");
            }

            if (zoomHookPayload == null)
            {
                _logger.LogError("Payload cannot be deserialized. Body: {body}", body);
            }

            return zoomHookPayload;
        }

        private bool ValidateSignature(HttpHeaders headers, string body)
        {
            if (!_zoomSignatureHelper.ValidateSignature(headers, body))
            {
                _logger.LogError("Invalid message signature. Headers: '{headers}'", headers.ToString());
                return false;
            }

            return true;
        }

        private async Task StoreSharingStartedEventAsync(ZoomHookPayload zoomHookPayload)
        {
            var zoomInfo = GetZoomPayloadInfo(zoomHookPayload);

            if (zoomInfo.roomId != _zoomOptions.MonitoredRoom)
            {
                _logger.LogDebug("RoomId '{roomId}' is not monitored", zoomInfo.roomId);
                return;
            }

            var currentUserSharingEntities = await GetCurrentSharingEntitiesAsync(zoomInfo.userId, zoomInfo.roomId);
            if (currentUserSharingEntities.Any())
            {
                throw new InvalidOperationException($"User '{zoomInfo.userId}' already has a sharing started event on room '{zoomInfo.roomId}'");
            }

            var entity = new ZoomHistoryEntity
            {
                PartitionKey = _tableOptions.PartitionKey,
                RowKey = Guid.NewGuid().ToString("N"),
                UserId = zoomInfo.userId,
                Username = zoomInfo.username,
                RoomId = zoomInfo.roomId,
                RoomName = zoomInfo.roomName,
                StartSharing = zoomInfo.timestamp
            };
            await _tableClient.AddEntityAsync(entity);
        }

        private async Task StoreSharingEndedEventAsync(ZoomHookPayload zoomHookPayload)
        {
            var zoomInfo = GetZoomPayloadInfo(zoomHookPayload);

            if (zoomInfo.roomId != _zoomOptions.MonitoredRoom)
            {
                _logger.LogDebug("RoomId '{roomId}' is not monitored", zoomInfo.roomId);
                return;
            }

            var currentUserSharingEntities = await GetCurrentSharingEntitiesAsync(zoomInfo.userId, zoomInfo.roomId);
            if (currentUserSharingEntities.Length == 1)
            {
                var entity = currentUserSharingEntities.Single();
                entity.EndSharing = zoomInfo.timestamp;
                await _tableClient.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace);
            }
            else
            {
                _logger.LogWarning("User '{userId}' has no sharing or multiple sharings active on room '{roomId}'", zoomInfo.userId, zoomInfo.roomId);
            }
        }

        private ValueTask<ZoomHistoryEntity[]> GetCurrentSharingEntitiesAsync(string userId, string roomId)
        {
            return _tableClient
                .QueryAsync<ZoomHistoryEntity>(_ => _.UserId == userId && _.RoomId == roomId)
                .Where(_ => _.EndSharing == null)
                .ToArrayAsync();
        }

        private (DateTimeOffset timestamp, string userId, string username, string roomId, string roomName) GetZoomPayloadInfo(ZoomHookPayload zoomHookPayload)
        {
            var userId = zoomHookPayload.Payload?.Object?.Participant?.UserId;
            if (userId == null)
            {
                throw new InvalidOperationException("UserId is null");
            }

            var username = zoomHookPayload.Payload?.Object?.Participant?.Username;
            if (username == null)
            {
                throw new InvalidOperationException("Username is null");
            }

            var roomId = zoomHookPayload.Payload?.Object?.RoomId;
            if (roomId == null)
            {
                throw new InvalidOperationException("RoomId is null");
            }

            var roomName = zoomHookPayload.Payload?.Object?.RoomTopic;
            if (roomName == null)
            {
                throw new InvalidOperationException("RoomTopic is null");
            }

            var timestamp = zoomHookPayload.Timestamp;
            if (!timestamp.HasValue)
            {
                throw new InvalidOperationException("Timestamp is null");
            }

            return (timestamp.Value, userId, username, roomId, roomName);
        }
    }
}