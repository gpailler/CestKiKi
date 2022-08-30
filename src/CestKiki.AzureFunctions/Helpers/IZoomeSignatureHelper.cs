using System.Net.Http.Headers;

namespace CestKiki.AzureFunctions.Helpers;

public interface IZoomSignatureHelper
{
    bool ValidateSignature(HttpHeaders headers, string payload);
}
