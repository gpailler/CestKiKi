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

            _logger.LogDebug("Function 'ZoomWebHook' called with:\n- Headers: '{headers}'\n- Body: '{body}'",
                string.Join(", ", request.Headers.Select(_ => $"{_.Key}: {string.Join(", ", _.Value)}")),
                body);

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
                _logger.LogInformation("Zoom event '{event}' received", zoomHookPayload.Event);
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
                _logger.LogError(ex, "Error while processing Zoom event '{event}'", zoomHookPayload.Event);
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
                _logger.LogError("Invalid message signature");
                return false;
            }

            return true;
        }

        private async Task StoreSharingStartedEventAsync(ZoomHookPayload zoomHookPayload)
        {
            var zoomInfo = GetZoomPayloadInfo(zoomHookPayload);

            if (zoomInfo.roomId != _zoomOptions.MonitoredRoom)
            {
                _logger.LogInformation("RoomId '{roomId}' is not monitored. Event skipped", zoomInfo.roomId);
                return;
            }

            var currentUserSharingEntities = await GetCurrentSharingEntitiesAsync(zoomInfo.userId, zoomInfo.roomId);
            if (currentUserSharingEntities.Any())
            {
                throw new InvalidOperationException($"User '{zoomInfo.userId}' has an existing sharing on room '{zoomInfo.roomId}' (entity: '{currentUserSharingEntities.First().RowKey}')");
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

            _logger.LogInformation("Adding entity '{entity}'", entity.RowKey);
            await _tableClient.AddEntityAsync(entity);
        }

        private async Task StoreSharingEndedEventAsync(ZoomHookPayload zoomHookPayload)
        {
            var zoomInfo = GetZoomPayloadInfo(zoomHookPayload);

            if (zoomInfo.roomId != _zoomOptions.MonitoredRoom)
            {
                _logger.LogInformation("RoomId '{roomId}' is not monitored. Event skipped", zoomInfo.roomId);
                return;
            }

            var currentUserSharingEntities = await GetCurrentSharingEntitiesAsync(zoomInfo.userId, zoomInfo.roomId);
            if (currentUserSharingEntities.Length == 1)
            {
                var entity = currentUserSharingEntities.Single();
                entity.EndSharing = zoomInfo.timestamp > entity.StartSharing ? zoomInfo.timestamp : entity.StartSharing;

                _logger.LogInformation("Updating entity '{entity}'", entity.RowKey);
                await _tableClient.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace);
            }
            else
            {
                _logger.LogWarning("User '{userId}' doesn't have a single existing sharing on room '{roomId}'", zoomInfo.userId, zoomInfo.roomId);
            }
        }

        private ValueTask<ZoomHistoryEntity[]> GetCurrentSharingEntitiesAsync(string userId, string roomId)
        {
            return _tableClient
                .QueryAsync<ZoomHistoryEntity>(_ => _.UserId == userId && _.RoomId == roomId)
                .Where(_ => _.EndSharing == null)
                .ToArrayAsync();
        }

        private static (DateTimeOffset timestamp, string userId, string username, string roomId, string roomName) GetZoomPayloadInfo(ZoomHookPayload zoomHookPayload)
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
