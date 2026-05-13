# UniRateApi.NodaMoney

[![NuGet](https://img.shields.io/nuget/v/UniRateApi.NodaMoney.svg)](https://www.nuget.org/packages/UniRateApi.NodaMoney/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

The first published [NodaMoney](https://github.com/RemyDuijkeren/NodaMoney) rate-source package: pull live and historical FX rates from [UniRate](https://unirateapi.com) and use them as native `NodaMoney.Exchange.ExchangeRate` values to convert `Money`.

NodaMoney 2.x ships `ExchangeRate` and a `Money.Currency` model but no built-in providers. Until now you had to hand-roll one. This package is a tiny shim — drop it in, give it an API key, get rates back as the NodaMoney type your codebase already uses.

## Install

```bash
dotnet add package UniRateApi.NodaMoney
```

Target frameworks: `net8.0`, `net9.0`, `net10.0`, `netstandard2.0`, `netstandard2.1`. Depends on `NodaMoney >= 2.7.0`.

## Quick start

```csharp
using NodaMoney;
using UniRateApi.NodaMoney;

using var provider = new UniRateExchangeRateProvider(apiKey: "YOUR_KEY");

// Fetch a rate and use NodaMoney's built-in Convert.
var usdEur = await provider.GetExchangeRateAsync("USD", "EUR");
var price  = new Money(199.95m, "USD");
Console.WriteLine(usdEur.Convert(price));  // EUR 183.95 (or w/e today's rate is)

// One-shot convert.
var inGbp = await provider.ConvertAsync(price, "GBP");
```

Sign up at [unirateapi.com](https://unirateapi.com) for a free key. Free tier covers live rates; historical rates and commodities require a Pro subscription.

## API

```csharp
public interface IUniRateExchangeRateProvider
{
    Task<ExchangeRate>          GetExchangeRateAsync(Currency baseCurrency, Currency quoteCurrency, CancellationToken ct = default);
    Task<ExchangeRate>          GetExchangeRateAsync(string baseCode, string quoteCode, CancellationToken ct = default);
    Task<IReadOnlyList<ExchangeRate>> GetAllExchangeRatesAsync(Currency baseCurrency, CancellationToken ct = default);
    Task<IReadOnlyList<ExchangeRate>> GetAllExchangeRatesAsync(string baseCode = "USD", CancellationToken ct = default);
    Task<Money>                 ConvertAsync(Money money, Currency targetCurrency, CancellationToken ct = default);
    Task<Money>                 ConvertAsync(Money money, string targetCode, CancellationToken ct = default);
    Task<ExchangeRate>          GetHistoricalExchangeRateAsync(string date, Currency baseCurrency, Currency quoteCurrency, CancellationToken ct = default);  // Pro
    Task<Money>                 ConvertHistoricalAsync(Money money, Currency targetCurrency, string date, CancellationToken ct = default);                    // Pro
    Task<IReadOnlyList<string>> GetSupportedCurrencyCodesAsync(CancellationToken ct = default);
}
```

`UniRateExchangeRateProvider` is the default implementation. It is thread-safe and designed to be long-lived — register it as a singleton.

### Dependency injection

```csharp
services.AddSingleton<IUniRateExchangeRateProvider>(_ =>
    new UniRateExchangeRateProvider(Configuration["UniRate:ApiKey"]!));
```

Or pair with `IHttpClientFactory`:

```csharp
services.AddHttpClient("unirate", c =>
{
    c.BaseAddress = new Uri("https://api.unirateapi.com");
    c.Timeout     = TimeSpan.FromSeconds(15);
});
services.AddSingleton<IUniRateExchangeRateProvider>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("unirate");
    return new UniRateExchangeRateProvider(Configuration["UniRate:ApiKey"]!, http);
});
```

## Errors

| HTTP | Exception |
|---|---|
| 401 | `UniRateAuthenticationException` |
| 403 | `UniRateProRequiredException` (historical / commodities on free tier) |
| 404 | `UniRateInvalidCurrencyException` |
| 429 | `UniRateRateLimitException` |
| other | `UniRateApiException` (with `StatusCode`, `Body`) |
| transport | `UniRateException` |

## Currency codes UniRate quotes that NodaMoney doesn't know

UniRate quotes a small number of non-ISO-4217 codes (some crypto, metals). `GetAllExchangeRatesAsync` silently skips any code `NodaMoney.CurrencyInfo.FromCode` cannot resolve. If you need full coverage, call the underlying API directly via your `HttpClient` or use the standalone [UniRate .NET client](https://github.com/UniRate-API/unirate-api-dotnet).

## License

MIT — see [LICENSE](LICENSE).
