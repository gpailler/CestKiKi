using System.Text;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

using Moq;

namespace CestKiki.AzureFunctions.Tests;

public static class RequestHelper
{
    public static Mock<HttpRequestData> CreateMock(string body, HttpHeadersCollection? headers = null)
    {
        var context = new Mock<FunctionContext>();

        var byteArray = Encoding.ASCII.GetBytes(body);
        var bodyStream = new MemoryStream(byteArray);

        var request = new Mock<HttpRequestData>(context.Object);
        request.Setup(_ => _.Headers).Returns(headers ?? new HttpHeadersCollection());
        request.Setup(_ => _.Body).Returns(bodyStream);
        request.Setup(_ => _.CreateResponse()).Returns(() =>
        {
            var response = new Mock<HttpResponseData>(context.Object);
            response.SetupProperty(_ => _.Headers, new HttpHeadersCollection());
            response.SetupProperty(_ => _.StatusCode);
            response.SetupProperty(_ => _.Body, new MemoryStream());
            return response.Object;
        });

        return request;
    }
}
