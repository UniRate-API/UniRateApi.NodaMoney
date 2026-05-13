# Changelog

All notable changes are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] — 2026-05-13

### Added
- Initial release.
- `IUniRateExchangeRateProvider` interface returning native NodaMoney types
  (`ExchangeRate`, `Money`).
- `UniRateExchangeRateProvider` implementation backed by the UniRate HTTP API
  (`https://api.unirateapi.com`), with both internal and externally-owned
  `HttpClient` constructors for `IHttpClientFactory` interop.
- Current-rate and historical-rate fetches; Pro-gated endpoints map to
  `UniRateProRequiredException`.
- Money conversion helpers (`ConvertAsync`, `ConvertHistoricalAsync`).
- Multi-target: `netstandard2.0`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`.
- 25 mock tests covering rate fetching, conversion, error mapping, and
  composability with `NodaMoney.ExchangeRate.Convert`.
