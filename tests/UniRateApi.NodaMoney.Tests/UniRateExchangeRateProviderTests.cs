using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NodaMoney;
using NodaMoney.Exchange;
using Xunit;

namespace UniRateApi.NodaMoney.Tests;

public class UniRateExchangeRateProviderTests
{
    private const string Key = "test-key";
    private static readonly Uri Base = new("https://example.test");

    private static (UniRateExchangeRateProvider Provider, MockHttpHandler Handler) BuildProvider()
    {
        var handler = new MockHttpHandler();
        var http = new HttpClient(handler) { BaseAddress = Base };
        var provider = new UniRateExchangeRateProvider(Key, http);
        return (provider, handler);
    }

    private static Dictionary<string, string> ParseQuery(Uri uri)
        => uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));

    // ---- Constructor validation ----

    [Fact]
    public void Constructor_RejectsEmptyApiKey()
    {
        Assert.Throws<ArgumentException>(() => new UniRateExchangeRateProvider(""));
        Assert.Throws<ArgumentException>(() => new UniRateExchangeRateProvider("  "));
    }

    [Fact]
    public void Constructor_RejectsNullHttpClient()
    {
        Assert.Throws<ArgumentNullException>(() => new UniRateExchangeRateProvider(Key, null!));
    }

    // ---- GetExchangeRateAsync (pair) ----

    [Fact]
    public async Task GetExchangeRateAsync_ByCurrencies_ReturnsRateAndSendsCorrectQuery()
    {
        var (provider, handler) = BuildProvider();
        handler.EnqueueOk("{\"rate\":\"0.92\"}");

        var rate = await provider.GetExchangeRateAsync(
            CurrencyInfo.FromCode("USD"),
            CurrencyInfo.FromCode("EUR"));

        Assert.Equal("USD", rate.BaseCurrency.Code);
        Assert.Equal("EUR", rate.QuoteCurrency.Code);
        Assert.Equal(0.92m, rate.Value);

        var req = handler.LastRequest.RequestUri!;
        Assert.Equal("/api/rates", req.AbsolutePath);
        var query = ParseQuery(req);
        Assert.Equal("USD", query["from"]);
        Assert.Equal("EUR", query["to"]);
        Assert.Equal(Key, query["api_key"]);
    }

    [Fact]
    public async Task GetExchangeRateAsync_ByCodes_NormalizesToUpper()
    {
        var (provider, handler) = BuildProvider();
        handler.EnqueueOk("{\"rate\":1.27}");

        var rate = await provider.GetExchangeRateAsync("gbp", "usd");

        Assert.Equal("GBP", rate.BaseCurrency.Code);
        Assert.Equal("USD", rate.QuoteCurrency.Code);
        Assert.Equal(1.27m, rate.Value);

        var query = ParseQuery(handler.LastRequest.RequestUri!);
        Assert.Equal("GBP", query["from"]);
        Assert.Equal("USD", query["to"]);
    }

    [Fact]
    public async Task GetExchangeRateAsync_AcceptsNumericJsonRate()
    {
        var (provider, handler) = BuildProvider();
        handler.EnqueueOk("{\"rate\":0.78953}");

        var rate = await provider.GetExchangeRateAsync("USD", "GBP");
        Assert.Equal(0.78953m, rate.Value);
    }

    // ---- GetAllExchangeRatesAsync ----

    [Fact]
    public async Task GetAllExchangeRatesAsync_ReturnsListAndSkipsUnknownCodes()
    {
        var (provider, handler) = BuildProvider();
        handler.EnqueueOk(@"{""rates"":{""EUR"":""0.92"",""GBP"":""0.79"",""ZZZ"":""1.00""}}");

        var rates = await provider.GetAllExchangeRatesAsync("USD");

        // ZZZ is not a real ISO-4217 code; should be skipped.
        Assert.Equal(2, rates.Count);
        Assert.All(rates, r => Assert.Equal("USD", r.BaseCurrency.Code));
        Assert.Contains(rates, r => r.QuoteCurrency.Code == "EUR" && r.Value == 0.92m);
        Assert.Contains(rates, r => r.QuoteCurrency.Code == "GBP" && r.Value == 0.79m);

        var query = ParseQuery(handler.LastRequest.RequestUri!);
        Assert.Equal("USD", query["from"]);
        Assert.False(query.ContainsKey("to"));
    }

    [Fact]
    public async Task GetAllExchangeRatesAsync_DefaultsBaseToUsd()
    {
        var (provider, handler) = BuildProvider();
        handler.EnqueueOk(@"{""rates"":{""EUR"":""0.92""}}");

        await provider.GetAllExchangeRatesAsync();

        var query = ParseQuery(handler.LastRequest.RequestUri!);
        Assert.Equal("USD", query["from"]);
    }

    // ---- ConvertAsync (Money in / Money out) ----

    [Fact]
    public async Task ConvertAsync_MultipliesUsingFetchedRate()
    {
        var (provider, handler) = BuildProvider();
        handler.EnqueueOk("{\"rate\":\"0.92\"}");

        var hundredUsd = new Money(100m, "USD");
        var inEur = await provider.ConvertAsync(hundredUsd, "EUR");

        Assert.Equal("EUR", inEur.Currency.Code);
        Assert.Equal(92m, inEur.Amount);

        var query = ParseQuery(handler.LastRequest.RequestUri!);
        Assert.Equal("USD", query["from"]);
        Assert.Equal("EUR", query["to"]);
    }

    [Fact]
    public async Task ConvertAsync_ReturnsSameMoneyWhenCurrenciesMatch_AndSkipsHttp()
    {
        var (provider, handler) = BuildProvider();

        var input = new Money(50m, "USD");
        var output = await provider.ConvertAsync(input, "USD");

        Assert.Equal(input, output);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ConvertAsync_ByCurrency_UsesGivenInstance()
    {
        var (provider, handler) = BuildProvider();
        handler.EnqueueOk("{\"rate\":\"1.27\"}");

        var input = new Money(10m, "GBP");
        var output = await provider.ConvertAsync(input, CurrencyInfo.FromCode("USD"));

        Assert.Equal("USD", output.Currency.Code);
        Assert.Equal(12.7m, output.Amount);
    }

    // ---- Historical ----

    [Fact]
    public async Task GetHistoricalExchangeRateAsync_SendsDateAndAmountQuery()
    {
        var (provider, handler) = BuildProvider();
        handler.EnqueueOk("{\"rate\":\"0.85\"}");

        var rate = await provider.GetHistoricalExchangeRateAsync(
            "2024-01-15",
            CurrencyInfo.FromCode("USD"),
            CurrencyInfo.FromCode("EUR"));

        Assert.Equal(0.85m, rate.Value);
        Assert.Equal("USD", rate.BaseCurrency.Code);
        Assert.Equal("EUR", rate.QuoteCurrency.Code);

        var req = handler.LastRequest.RequestUri!;
        Assert.Equal("/api/historical/rates", req.AbsolutePath);
        var query = ParseQuery(req);
        Assert.Equal("2024-01-15", query["date"]);
        Assert.Equal("1", query["amount"]);
        Assert.Equal("USD", query["from"]);
        Assert.Equal("EUR", query["to"]);
    }

    [Fact]
    public async Task GetHistoricalExchangeRateAsync_RejectsEmptyDate()
    {
        var (provider, _) = BuildProvider();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GetHistoricalExchangeRateAsync(
                "",
                CurrencyInfo.FromCode("USD"),
                CurrencyInfo.FromCode("EUR")));
    }

    [Fact]
    public async Task ConvertHistoricalAsync_UsesHistoricalEndpoint()
    {
        var (provider, handler) = BuildProvider();
        handler.EnqueueOk("{\"rate\":\"0.85\"}");

        var output = await provider.ConvertHistoricalAsync(
            new Money(200m, "USD"),
            CurrencyInfo.FromCode("EUR"),
            "2024-01-15");

        Assert.Equal("EUR", output.Currency.Code);
        Assert.Equal(170m, output.Amount);
        Assert.Equal("/api/historical/rates", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    // ---- Supported currencies ----

    [Fact]
    public async Task GetSupportedCurrencyCodesAsync_ParsesList()
    {
        var (provider, handler) = BuildProvider();
        handler.EnqueueOk(@"{""currencies"":[""USD"",""EUR"",""GBP""]}");

        var codes = await provider.GetSupportedCurrencyCodesAsync();
        Assert.Equal(new[] { "USD", "EUR", "GBP" }, codes);

        Assert.Equal("/api/currencies", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task RequestsCarryAcceptJsonHeader()
    {
        var (provider, handler) = BuildProvider();
        handler.EnqueueOk("{\"rate\":\"1\"}");

        await provider.GetExchangeRateAsync("USD", "USD");

        var accept = handler.LastRequest.Headers.Accept;
        Assert.Contains(accept, h => h.MediaType == "application/json");
    }

    // ---- Error mapping ----

    [Fact]
    public async Task AuthErrorThrowsAuthenticationException()
    {
        var (provider, handler) = BuildProvider();
        handler.Enqueue(HttpStatusCode.Unauthorized, "bad key");
        await Assert.ThrowsAsync<UniRateAuthenticationException>(
            () => provider.GetExchangeRateAsync("USD", "EUR"));
    }

    [Fact]
    public async Task ForbiddenThrowsProRequiredException()
    {
        var (provider, handler) = BuildProvider();
        handler.Enqueue(HttpStatusCode.Forbidden, "{\"error\":\"pro only\"}");
        var ex = await Assert.ThrowsAsync<UniRateProRequiredException>(
            () => provider.GetHistoricalExchangeRateAsync(
                "2024-01-01",
                CurrencyInfo.FromCode("USD"),
                CurrencyInfo.FromCode("EUR")));
        Assert.Contains("pro only", ex.Message);
    }

    [Fact]
    public async Task NotFoundThrowsInvalidCurrencyException()
    {
        var (provider, handler) = BuildProvider();
        handler.Enqueue(HttpStatusCode.NotFound, "not found");
        await Assert.ThrowsAsync<UniRateInvalidCurrencyException>(
            () => provider.GetExchangeRateAsync("USD", "EUR"));
    }

    [Fact]
    public async Task TooManyRequestsThrowsRateLimitException()
    {
        var (provider, handler) = BuildProvider();
        handler.Enqueue((HttpStatusCode)429, "slow down");
        await Assert.ThrowsAsync<UniRateRateLimitException>(
            () => provider.GetExchangeRateAsync("USD", "EUR"));
    }

    [Fact]
    public async Task FiveHundredThrowsApiException()
    {
        var (provider, handler) = BuildProvider();
        handler.Enqueue(HttpStatusCode.InternalServerError, "boom");
        var ex = await Assert.ThrowsAsync<UniRateApiException>(
            () => provider.GetExchangeRateAsync("USD", "EUR"));
        Assert.Equal(500, ex.StatusCode);
        Assert.Equal("boom", ex.Body);
    }

    [Fact]
    public async Task EmptyBodyThrowsUniRateException()
    {
        var (provider, handler) = BuildProvider();
        handler.EnqueueOk("");
        await Assert.ThrowsAsync<UniRateException>(
            () => provider.GetExchangeRateAsync("USD", "EUR"));
    }

    [Fact]
    public async Task InvalidJsonThrowsUniRateException()
    {
        var (provider, handler) = BuildProvider();
        handler.EnqueueOk("not-json");
        await Assert.ThrowsAsync<UniRateException>(
            () => provider.GetExchangeRateAsync("USD", "EUR"));
    }

    [Fact]
    public async Task GetExchangeRateAsync_RejectsEmptyCode()
    {
        var (provider, _) = BuildProvider();
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.GetExchangeRateAsync("", "EUR"));
    }

    // ---- ExchangeRate.Convert composes with our returned rate ----

    [Fact]
    public async Task ReturnedExchangeRate_ConvertsMoneyDirectly()
    {
        var (provider, handler) = BuildProvider();
        handler.EnqueueOk("{\"rate\":\"0.92\"}");

        var rate = await provider.GetExchangeRateAsync("USD", "EUR");
        var converted = rate.Convert(new Money(250m, "USD"));

        Assert.Equal("EUR", converted.Currency.Code);
        Assert.Equal(230m, converted.Amount);
    }

    [Fact]
    public async Task ReturnedExchangeRate_ConvertsReverseDirection()
    {
        var (provider, handler) = BuildProvider();
        handler.EnqueueOk("{\"rate\":\"0.92\"}");

        var rate = await provider.GetExchangeRateAsync("USD", "EUR");
        // Convert EUR back into USD using the same rate (NodaMoney divides).
        var back = rate.Convert(new Money(92m, "EUR"));
        Assert.Equal("USD", back.Currency.Code);
        Assert.Equal(100m, back.Amount);
    }
}
