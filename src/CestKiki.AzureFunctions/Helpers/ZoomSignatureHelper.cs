using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

using CestKiki.AzureFunctions.Options;

using Microsoft.Extensions.Options;

namespace CestKiki.AzureFunctions.Helpers;

public class ZoomSignatureHelper : IZoomSignatureHelper
{
    private readonly string _zoomSecret;

    public ZoomSignatureHelper(IOptions<ZoomOptions> options)
    {
        _zoomSecret = options.Value.WebHookSecret ?? throw new ArgumentException("ZoomWebHookSecret is not set");
    }

    public bool ValidateSignature(HttpHeaders headers, string payload)
    {
        if (headers.TryGetValues("x-zm-request-timestamp", out var timestamps)
            && long.TryParse(timestamps.FirstOrDefault(), out var timestamp)
            && headers.TryGetValues("x-zm-signature", out var signatures))
        {
            var computedSignature = GenerateSignature(payload, DateTimeOffset.FromUnixTimeMilliseconds(timestamp));

            return string.Equals(computedSignature, signatures.First(), StringComparison.InvariantCultureIgnoreCase);
        }

        return false;
    }

    private string GenerateSignature(string payload, DateTimeOffset timestamp)
    {
        var message = $"v0:{timestamp.ToUnixTimeMilliseconds()}:{payload}";
        var hash = HMACSHA256.HashData(Encoding.ASCII.GetBytes(_zoomSecret), Encoding.ASCII.GetBytes(message));
        var computedSignature = "v0=" + BitConverter.ToString(hash).Replace("-", "");
        return computedSignature;
    }
}
