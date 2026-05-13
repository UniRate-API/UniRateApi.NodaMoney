using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NodaMoney;
using NodaMoney.Exchange;

namespace UniRateApi.NodaMoney;

/// <summary>
/// Default <see cref="IUniRateExchangeRateProvider"/> backed by the UniRate
/// HTTP API at <see cref="DefaultBaseUrl"/>.
/// </summary>
/// <remarks>
/// Thread-safe and designed to be long-lived: register as a singleton when used
/// with dependency injection, or share a single instance across an application.
/// Prefer the <see cref="UniRateExchangeRateProvider(string, HttpClient)"/>
/// overload when you already have an <c>IHttpClientFactory</c>.
/// </remarks>
public sealed class UniRateExchangeRateProvider : IUniRateExchangeRateProvider, IDisposable
{
    /// <summary>Default UniRate API base URL.</summary>
    public const string DefaultBaseUrl = "https://api.unirateapi.com";

    /// <summary>Bridge package version; must match the .csproj <c>Version</c>.</summary>
    public const string ProviderVersion = "0.1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        Converters =
        {
            new FlexibleDecimalConverter(),
        },
    };

    private readonly string _apiKey;
    private readonly Uri _baseUrl;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Creates a provider with a freshly-configured internal <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="apiKey">API key (required).</param>
    /// <param name="baseUrl">Optional base URL override.</param>
    /// <param name="timeout">HTTP timeout (default 30 seconds).</param>
    public UniRateExchangeRateProvider(string apiKey, string? baseUrl = null, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key must not be empty", nameof(apiKey));

        _apiKey = apiKey;
        _baseUrl = new Uri(baseUrl ?? DefaultBaseUrl);
        _httpClient = new HttpClient
        {
            BaseAddress = _baseUrl,
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"unirate-nodamoney/{ProviderVersion}");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _ownsHttpClient = true;
    }

    /// <summary>
    /// Creates a provider wrapping an externally-owned <see cref="HttpClient"/>.
    /// The caller retains ownership and is responsible for disposal. Use this
    /// overload with <c>IHttpClientFactory</c> or tests that inject a custom
    /// <see cref="HttpMessageHandler"/>.
    /// </summary>
    public UniRateExchangeRateProvider(string apiKey, HttpClient httpClient)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key must not be empty", nameof(apiKey));
        if (httpClient is null) throw new ArgumentNullException(nameof(httpClient));

        _apiKey = apiKey;
        _httpClient = httpClient;
        _baseUrl = httpClient.BaseAddress ?? new Uri(DefaultBaseUrl);
        _ownsHttpClient = false;

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"unirate-nodamoney/{ProviderVersion}");
        if (!_httpClient.DefaultRequestHeaders.Accept.Any(h => h.MediaType == "application/json"))
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // ------------------------------------------------------------------
    // Current rates
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<ExchangeRate> GetExchangeRateAsync(
        Currency baseCurrency,
        Currency quoteCurrency,
        CancellationToken cancellationToken = default)
    {
        var body = await RequestAsync<RateResponse>(
            "/api/rates",
            new Dictionary<string, string?>
            {
                ["from"] = baseCurrency.Code.ToUpperInvariant(),
                ["to"] = quoteCurrency.Code.ToUpperInvariant(),
            },
            cancellationToken).ConfigureAwait(false);
        return new ExchangeRate(baseCurrency, quoteCurrency, body.Rate);
    }

    /// <inheritdoc />
    public Task<ExchangeRate> GetExchangeRateAsync(
        string baseCode,
        string quoteCode,
        CancellationToken cancellationToken = default)
        => GetExchangeRateAsync(
            CurrencyInfo.FromCode(RequireCode(baseCode, nameof(baseCode))),
            CurrencyInfo.FromCode(RequireCode(quoteCode, nameof(quoteCode))),
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExchangeRate>> GetAllExchangeRatesAsync(
        Currency baseCurrency,
        CancellationToken cancellationToken = default)
    {
        var body = await RequestAsync<RatesResponse>(
            "/api/rates",
            new Dictionary<string, string?>
            {
                ["from"] = baseCurrency.Code.ToUpperInvariant(),
            },
            cancellationToken).ConfigureAwait(false);

        var list = new List<ExchangeRate>(body.Rates.Count);
        foreach (var kvp in body.Rates)
        {
            if (!TryResolveCurrency(kvp.Key, out var quote)) continue;
            list.Add(new ExchangeRate(baseCurrency, quote, kvp.Value));
        }
        return list;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ExchangeRate>> GetAllExchangeRatesAsync(
        string baseCode = "USD",
        CancellationToken cancellationToken = default)
        => GetAllExchangeRatesAsync(
            CurrencyInfo.FromCode(RequireCode(baseCode, nameof(baseCode))),
            cancellationToken);

    /// <inheritdoc />
    public async Task<Money> ConvertAsync(
        Money money,
        Currency targetCurrency,
        CancellationToken cancellationToken = default)
    {
        if (money.Currency == targetCurrency) return money;
        var rate = await GetExchangeRateAsync(money.Currency, targetCurrency, cancellationToken)
            .ConfigureAwait(false);
        return rate.Convert(money);
    }

    /// <inheritdoc />
    public Task<Money> ConvertAsync(
        Money money,
        string targetCode,
        CancellationToken cancellationToken = default)
        => ConvertAsync(
            money,
            CurrencyInfo.FromCode(RequireCode(targetCode, nameof(targetCode))),
            cancellationToken);

    // ------------------------------------------------------------------
    // Historical (Pro-gated)
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<ExchangeRate> GetHistoricalExchangeRateAsync(
        string date,
        Currency baseCurrency,
        Currency quoteCurrency,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(date))
            throw new ArgumentException("Date must be a non-empty YYYY-MM-DD string", nameof(date));

        var body = await RequestAsync<RateResponse>(
            "/api/historical/rates",
            new Dictionary<string, string?>
            {
                ["date"] = date,
                ["amount"] = "1",
                ["from"] = baseCurrency.Code.ToUpperInvariant(),
                ["to"] = quoteCurrency.Code.ToUpperInvariant(),
            },
            cancellationToken).ConfigureAwait(false);
        return new ExchangeRate(baseCurrency, quoteCurrency, body.Rate);
    }

    /// <inheritdoc />
    public async Task<Money> ConvertHistoricalAsync(
        Money money,
        Currency targetCurrency,
        string date,
        CancellationToken cancellationToken = default)
    {
        if (money.Currency == targetCurrency) return money;
        var rate = await GetHistoricalExchangeRateAsync(date, money.Currency, targetCurrency, cancellationToken)
            .ConfigureAwait(false);
        return rate.Convert(money);
    }

    // ------------------------------------------------------------------
    // Supported currencies
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetSupportedCurrencyCodesAsync(
        CancellationToken cancellationToken = default)
    {
        var body = await RequestAsync<CurrenciesResponse>(
            "/api/currencies",
            new Dictionary<string, string?>(),
            cancellationToken).ConfigureAwait(false);
        return body.Currencies;
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private static string RequireCode(string code, string paramName)
        => string.IsNullOrWhiteSpace(code)
            ? throw new ArgumentException("Currency code must not be empty", paramName)
            : code.Trim().ToUpperInvariant();

    private static bool TryResolveCurrency(string code, out Currency currency)
    {
        try
        {
            currency = CurrencyInfo.FromCode(code);
            return true;
        }
        catch (InvalidCurrencyException)
        {
            // UniRate quotes some non-ISO-4217 codes (e.g. some crypto and metals);
            // skip them when assembling NodaMoney ExchangeRate values.
            currency = default;
            return false;
        }
    }

    private async Task<T> RequestAsync<T>(
        string path,
        IDictionary<string, string?> query,
        CancellationToken cancellationToken)
    {
        var url = BuildUrl(path, query);
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // Defaults on _httpClient cover Accept, but some test handlers
            // bypass defaults — keep this set on every outgoing request.
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new UniRateException($"Network error: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UniRateException($"Request timed out: {ex.Message}", ex);
        }

        using (response)
        {
#if NET5_0_OR_GREATER
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif

            switch ((int)response.StatusCode)
            {
                case >= 200 and < 300:
                    break;
                case 400:
                    throw new UniRateApiException(400, body, "UniRate API rejected the request");
                case 401:
                    throw new UniRateAuthenticationException();
                case 403:
                    throw new UniRateProRequiredException(body);
                case 404:
                    throw new UniRateInvalidCurrencyException();
                case 429:
                    throw new UniRateRateLimitException();
                default:
                    throw new UniRateApiException((int)response.StatusCode, body);
            }

            if (string.IsNullOrWhiteSpace(body))
                throw new UniRateException("Empty response body from UniRate");

            try
            {
                var parsed = JsonSerializer.Deserialize<T>(body, JsonOptions);
                if (parsed is null)
                    throw new UniRateException("Response deserialized to null");
                return parsed;
            }
            catch (JsonException ex)
            {
                throw new UniRateException($"Failed to decode UniRate response: {ex.Message}", ex);
            }
        }
    }

    private Uri BuildUrl(string path, IDictionary<string, string?> query)
    {
        var parts = new List<string>(query.Count + 1);
        foreach (var kvp in query)
        {
            if (kvp.Value is null) continue;
            parts.Add($"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}");
        }
        parts.Add($"api_key={Uri.EscapeDataString(_apiKey)}");

        var trimmedBase = _baseUrl.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        var trimmedPath = path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
        return new Uri($"{trimmedBase}{trimmedPath}?{string.Join("&", parts)}");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
    }

    // ------------------------------------------------------------------
    // Response DTOs
    // ------------------------------------------------------------------

    private sealed class RateResponse
    {
        [JsonPropertyName("rate")]
        public decimal Rate { get; set; }
    }

    private sealed class RatesResponse
    {
        [JsonPropertyName("rates")]
        public IReadOnlyDictionary<string, decimal> Rates { get; set; }
            = new Dictionary<string, decimal>();
    }

    private sealed class CurrenciesResponse
    {
        [JsonPropertyName("currencies")]
        public IReadOnlyList<string> Currencies { get; set; } = Array.Empty<string>();
    }

    // ------------------------------------------------------------------
    // JSON converter — UniRate returns rate values as JSON strings in some
    // shapes and as numbers in others; accept both.
    // ------------------------------------------------------------------

    private sealed class FlexibleDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();
                if (decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    return d;
                throw new JsonException($"Cannot parse '{s}' as decimal");
            }
            if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetDecimal();
            }
            throw new JsonException($"Unexpected token {reader.TokenType} when parsing decimal");
        }

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
    }
}
