using System;

namespace UniRateApi.NodaMoney;

/// <summary>Base exception for all UniRate-NodaMoney bridge errors.</summary>
public class UniRateException : Exception
{
    public UniRateException(string message) : base(message) { }
    public UniRateException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Thrown when the API rejects the supplied API key (HTTP 401).</summary>
public sealed class UniRateAuthenticationException : UniRateException
{
    public UniRateAuthenticationException()
        : base("Invalid or missing UniRate API key. Sign up at https://unirateapi.com for a free key.") { }
}

/// <summary>
/// Thrown when the request requires a Pro subscription (HTTP 403).
/// Historical rates and commodities are Pro-gated on UniRate's free tier.
/// </summary>
public sealed class UniRateProRequiredException : UniRateException
{
    public UniRateProRequiredException(string body)
        : base($"Endpoint requires a UniRate Pro subscription. Server said: {body}") { }
}

/// <summary>Thrown when the supplied currency code is unknown to UniRate (HTTP 404).</summary>
public sealed class UniRateInvalidCurrencyException : UniRateException
{
    public UniRateInvalidCurrencyException()
        : base("Unknown currency code.") { }
}

/// <summary>Thrown when the rate limit has been exceeded (HTTP 429).</summary>
public sealed class UniRateRateLimitException : UniRateException
{
    public UniRateRateLimitException()
        : base("UniRate API rate limit exceeded.") { }
}

/// <summary>Thrown for any other non-2xx response, including 5xx errors.</summary>
public sealed class UniRateApiException : UniRateException
{
    public int StatusCode { get; }
    public string Body { get; }

    public UniRateApiException(int statusCode, string body, string? message = null)
        : base(message ?? $"UniRate API returned HTTP {statusCode}: {body}")
    {
        StatusCode = statusCode;
        Body = body;
    }
}
