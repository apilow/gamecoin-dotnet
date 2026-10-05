# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project adheres to
[Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-10-05

First pre-release.

### Added
- `GameCoinClient` with `Catalog`, `Players`, `Wallet`, `Ledger`, `Inventory`, `Purchases`, `Checkouts` and `Orders`
  resources covering the whole GameCoin server API v1.
- `PlayerRef.Ext(...)` helper; path segments are URL-encoded exactly once.
- Idempotency keys on every mutation (UUID v4 generated once per call and kept across retries, or your own through
  `RequestOptions`), and a `Replayed` flag on the results.
- Automatic retries on network errors, timeouts, `5xx` and `429` (exponential backoff with jitter, `Retry-After` honoured
  up to 30 s). Refunds are retried on `429` only.
- `GameCoinException` with `Code`, `Status`, `Details` and `RetryAfter`, and the `GameCoinErrorCodes` constants.
- `Ledger.IterateAsync` as an `IAsyncEnumerable<LedgerEntry>`.
- Strict checking of response shapes: a body that is not the expected JSON, or has a field of the wrong shape, is
  `INVALID_RESPONSE` (the HTTP status is kept).
- Targets `net8.0` (no dependency) and `netstandard2.1` (`System.Text.Json` only); trimming and AOT friendly
  (source-generated JSON).
