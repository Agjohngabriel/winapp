// src/AutoConnect.Client/Configuration/RetryPolicyConfiguration.cs
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace AutoConnect.Client.Configuration;

public static class RetryPolicyConfiguration
{
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError() // Handles HttpRequestException, 5xx, and 408 responses
            .OrResult(msg => !msg.IsSuccessStatusCode && ShouldRetry(msg.StatusCode))
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Exponential backoff
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    var logger = context.GetLogger();
                    if (outcome.Exception != null)
                    {
                        logger?.LogWarning("HTTP retry attempt {RetryCount} after {Delay}ms due to exception: {Exception}",
                            retryCount, timespan.TotalMilliseconds, outcome.Exception.Message);
                    }
                    else if (outcome.Result != null)
                    {
                        logger?.LogWarning("HTTP retry attempt {RetryCount} after {Delay}ms due to status code: {StatusCode}",
                            retryCount, timespan.TotalMilliseconds, outcome.Result.StatusCode);
                    }
                });
    }

    public static IAsyncPolicy GetGeneralRetryPolicy()
    {
        return Policy
            .Handle<Exception>(ex => IsTransientError(ex))
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    var logger = context.GetLogger();
                    logger?.LogWarning("General retry attempt {RetryCount} after {Delay}ms due to: {Exception}",
                        retryCount, timespan.TotalMilliseconds, exception.Message);
                });
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.RequestTimeout => true,
            HttpStatusCode.InternalServerError => true,
            HttpStatusCode.BadGateway => true,
            HttpStatusCode.ServiceUnavailable => true,
            HttpStatusCode.GatewayTimeout => true,
            HttpStatusCode.TooManyRequests => true,
            _ => false
        };
    }

    private static bool IsTransientError(Exception exception)
    {
        return exception switch
        {
            HttpRequestException => true,
            TaskCanceledException => true,
            TimeoutException => true,
            SocketException => true,
            InvalidOperationException ex when ex.Message.Contains("connection") => true,
            _ => false
        };
    }
}

// Extension method for getting logger from Polly context
public static class PollyContextExtensions
{
    public static Microsoft.Extensions.Logging.ILogger? GetLogger(this Context context)
    {
        context.TryGetValue("logger", out var logger);
        return logger as Microsoft.Extensions.Logging.ILogger;
    }

    public static Context WithLogger(this Context context, Microsoft.Extensions.Logging.ILogger logger)
    {
        context["logger"] = logger;
        return context;
    }
}