# DigitalBrain.Capabilities.Http

The `http@1` capability contract: read-only HTTP for units.

- `IHttpRead` (`[Capability("http", 1)]`): `GetJson` and `GetText`.
- `HttpConnectionOptions(AllowedHosts)`: the settings of a connection. Entries are exact hosts or `*.example.org`.
- `HttpHosts.Require(url, allowed)`: the allow-list check every implementation calls; a miss, or a scheme other than http/https,
  throws `HostNotAllowedException`.

The package carries no HTTP client; the module that provides the connection does.
