using System;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Sitecore.API.Foundation.Tests;

internal static class LoggerTestExtensions
{
    public static void ShouldHaveLogged(this ILogger logger, LogLevel level, string expectedSubstring)
        => logger.ShouldHaveLogged(level, message => message.Contains(expectedSubstring, StringComparison.Ordinal));

    public static void ShouldHaveLogged(this ILogger logger, LogLevel level, Func<string, bool> messagePredicate)
    {
        logger.Received().Log(
            level,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => messagePredicate(o.ToString()!)),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    public static void ShouldNotHaveLogged(this ILogger logger, string forbiddenSubstring)
        => logger.ShouldNotHaveLogged(message => message.Contains(forbiddenSubstring, StringComparison.Ordinal));

    public static void ShouldNotHaveLogged(this ILogger logger, Func<string, bool> messagePredicate)
    {
        logger.DidNotReceive().Log(
            Arg.Any<LogLevel>(),
            Arg.Any<EventId>(),
            Arg.Is<object>(o => messagePredicate(o.ToString()!)),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
