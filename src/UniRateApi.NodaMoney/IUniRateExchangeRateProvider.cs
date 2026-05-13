using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NodaMoney;
using NodaMoney.Exchange;

namespace UniRateApi.NodaMoney;

/// <summary>
/// Fetches NodaMoney <see cref="ExchangeRate"/> values from the UniRate API.
/// </summary>
/// <remarks>
/// NodaMoney 2.x does not ship a built-in rate-provider interface; this contract
/// is defined here so that consumer code can be written against an abstraction
/// and unit-tested with a fake. Live and historical rate fetches both return
/// NodaMoney-native types so they compose directly with <c>Money</c>.
/// </remarks>
public interface IUniRateExchangeRateProvider
{
    /// <summary>Fetches the current exchange rate for a single currency pair.</summary>
    Task<ExchangeRate> GetExchangeRateAsync(
        Currency baseCurrency,
        Currency quoteCurrency,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches the current exchange rate for a pair of ISO-4217 codes.</summary>
    Task<ExchangeRate> GetExchangeRateAsync(
        string baseCode,
        string quoteCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches every supported quote rate for a base currency in one call.
    /// </summary>
    Task<IReadOnlyList<ExchangeRate>> GetAllExchangeRatesAsync(
        Currency baseCurrency,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches every supported quote rate for a base ISO-4217 code in one call.
    /// </summary>
    Task<IReadOnlyList<ExchangeRate>> GetAllExchangeRatesAsync(
        string baseCode = "USD",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts the given <see cref="Money"/> instance into <paramref name="targetCurrency"/>
    /// using the current exchange rate.
    /// </summary>
    Task<Money> ConvertAsync(
        Money money,
        Currency targetCurrency,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts the given <see cref="Money"/> instance into the currency named by
    /// <paramref name="targetCode"/> using the current exchange rate.
    /// </summary>
    Task<Money> ConvertAsync(
        Money money,
        string targetCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a historical exchange rate for a single currency pair on the
    /// given <paramref name="date"/> (ISO format <c>YYYY-MM-DD</c>).
    /// <para>Pro-gated: returns <see cref="UniRateProRequiredException"/> on the free tier.</para>
    /// </summary>
    Task<ExchangeRate> GetHistoricalExchangeRateAsync(
        string date,
        Currency baseCurrency,
        Currency quoteCurrency,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts <see cref="Money"/> using a historical exchange rate for the
    /// given <paramref name="date"/> (ISO format <c>YYYY-MM-DD</c>).
    /// <para>Pro-gated.</para>
    /// </summary>
    Task<Money> ConvertHistoricalAsync(
        Money money,
        Currency targetCurrency,
        string date,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the set of currency codes UniRate can quote.</summary>
    Task<IReadOnlyList<string>> GetSupportedCurrencyCodesAsync(
        CancellationToken cancellationToken = default);
}
