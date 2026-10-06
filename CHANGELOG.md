# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project adheres to
[Semantic Versioning](https://semver.org/).

## [0.2.0] - Unreleased

0.2.0 — `locale` on checkouts; webhook signature verification.

### Added
- `CheckoutRequest.Locale` (`fr`, `en` or `nl`, constants in `CheckoutLocale`): the language of the hosted payment page, the
  receipt and the e-mails of the order. Not sent when `null`.
- `Webhooks.ConstructEvent(rawBody, signatureHeader, secret, options)` (raw body as `string` or `byte[]`) verifies the
  `GameCoin-Signature` header (HMAC-SHA256, every `v1` compared in constant time, timestamp tolerance of 300 s by default) and
  returns a typed `WebhookEvent`. A pure function: no client, no network.
- `WebhookSignatureException` with a `Reason` (`WebhookSignatureReason`: `missing_header`, `malformed_header`,
  `no_matching_signature`, `timestamp_out_of_tolerance`, `invalid_payload`).
- Typed event data (`Order`, `WalletCreditedData`, `WebhookTestData`), the `WebhookEventTypes` constants, and an unknown event
  type passed through as a `JsonElement`.
- `examples/Webhook`: an ASP.NET minimal API endpoint.

## [0.1.0] - Unreleased

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
