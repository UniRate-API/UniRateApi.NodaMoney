using NodaMoney;
using UniRateApi.NodaMoney;

var apiKey = Environment.GetEnvironmentVariable("UNIRATE_API_KEY")
    ?? throw new InvalidOperationException("Set UNIRATE_API_KEY before running.");

using var provider = new UniRateExchangeRateProvider(apiKey);

// 1) Fetch a single exchange rate and use NodaMoney's built-in conversion.
var usdEur = await provider.GetExchangeRateAsync("USD", "EUR");
Console.WriteLine($"Rate: {usdEur}");

var price = new Money(199.95m, "USD");
var inEur = usdEur.Convert(price);
Console.WriteLine($"{price} = {inEur}");

// 2) One-shot convert: provider fetches the rate and applies it.
var inGbp = await provider.ConvertAsync(price, "GBP");
Console.WriteLine($"{price} = {inGbp}");

// 3) Walk all USD-based rates.
var allFromUsd = await provider.GetAllExchangeRatesAsync("USD");
Console.WriteLine($"USD quotes against {allFromUsd.Count} currencies");
foreach (var rate in allFromUsd.Take(5))
{
    Console.WriteLine($"  {rate}");
}
