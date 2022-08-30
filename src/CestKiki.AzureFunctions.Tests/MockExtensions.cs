using System.Linq.Expressions;

using Microsoft.Extensions.Logging;
using Moq;

namespace CestKiki.AzureFunctions.Tests;

public static class MockExtensions
{
    public static void VerifyLog<TLogger>(
        this Mock<ILogger<TLogger>> @this,
        LogLevel logLevel,
        string message)
    {
        @this.Verify(logger => logger.Log(
            It.Is<LogLevel>(_ => _ == logLevel),
            It.Is<EventId>(_ => _.Id == 0),
            It.Is<It.IsAnyType>((@object, @type) =>
                @object.ToString() == message && @type.Name == "FormattedLogValues"),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
    }

    public static void VerifyLog<TLogger, TException>(
        this Mock<ILogger<TLogger>> @this,
        LogLevel logLevel,
        string message,
        Expression<Func<TException, bool>> exceptionMatch)
        where TException : Exception
    {
        @this.Verify(logger => logger.Log(
            It.Is<LogLevel>(_ => _ == logLevel),
            It.Is<EventId>(_ => _.Id == 0),
            It.Is<It.IsAnyType>((@object, @type) =>
                @object.ToString() == message && @type.Name == "FormattedLogValues"),
            It.Is(exceptionMatch),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
    }
}
