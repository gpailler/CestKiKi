using CestKiki.AzureFunctions.Helpers;
using CestKiki.AzureFunctions.Options;

using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Options;

using Moq;

namespace CestKiki.AzureFunctions.Tests.Helpers;

public class ZoomSignatureHelperTests
{
    private const string TestWebHookSecret = "W9D5rdMF6yCyNVWCeNyihs";

    private readonly ZoomSignatureHelper _sut;

    public ZoomSignatureHelperTests()
    {
        var options = Mock.Of<IOptions<ZoomOptions>>(_ => _.Value.WebHookSecret == TestWebHookSecret);
        _sut = new ZoomSignatureHelper(options);
    }

    [Theory]
    [InlineData("", "v0=6CFA790F8A0984B86C8EC53848852B083D9EEC167E432A87FE7E9A6FAE1789B6", 0L, true)]
    [InlineData("", "v0=6CFA790F8A0984B86C8EC53848852B083D9EEC167E432A87FE7E9A6FAE1789B6", 1L, false)]
    [InlineData("", "v0=6CFA790F8A0984B86C8EC53848852B083D9EEC167E432A87FE7E9A6FAE1789B6foo", 0L, false)]
    [InlineData("{}", "v0=1A205C5B42C42727145DE1ECD534E1158746ED8700057540C85A33D5CE04AF09", 0L, true)]
    [InlineData("{}", "v0=1A205C5B42C42727145DE1ECD534E1158746ED8700057540C85A33D5CE04AF09", 1L, false)]
    [InlineData("{}", "v0=1A205C5B42C42727145DE1ECD534E1158746ED8700057540C85A33D5CE04AF09foo", 0L, false)]
    [InlineData("", "v0=384D66045DF916BC3C22E258C533712A084BF0C1EA38082995A32A67064795ED", 1661138716L, true)]
    [InlineData("{}", "v0=9FD47C513997ED00759862FCFB751B26A54DB9CDD40E38336E0FE92994E66D27", 1661138716L, true)]
    [InlineData(@"{ foo = ""bar"" }", "v0=6D19A848F1DB05BA480CDE24D6B2EFB8A69234C8B38A2456C0900F928199FAB6", 1661138716L, true)]
    public void ValidateSignature_Succeeds(string payload, string? signature, long? timestamp, bool expectedResult)
    {
        // Arrange
        var headers = new HttpHeadersCollection();
        if (signature != null)
        {
            headers.Add("x-zm-signature", signature);
        }
        if (timestamp.HasValue)
        {
            headers.Add("x-zm-request-timestamp", timestamp.Value.ToString());
        }

        // Act
        var result = _sut.ValidateSignature(headers, payload);

        // Assert
        Assert.Equal(expectedResult, result);
    }
}
