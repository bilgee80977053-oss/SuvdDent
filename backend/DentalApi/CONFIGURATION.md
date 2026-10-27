# Backend configuration

Set secrets via environment variables or a private secret manager. ASP.NET Core maps
double underscores (`__`) to `:` in configuration paths. Do not put credentials in
`appsettings.json` or commit a populated `appsettings.Development.json`.

| Environment variable | Purpose |
| --- | --- |
| `ConnectionStrings__Default` | SQLite database connection string |
| `Jwt__Key` | Clinic JWT signing key (at least 32 UTF-8 bytes) |
| `Jwt__Issuer`, `Jwt__Audience` | Clinic JWT issuer and audience |
| `PlatformJwt__Key` | Separate Platform JWT signing key (at least 32 UTF-8 bytes) |
| `PlatformJwt__Issuer`, `PlatformJwt__Audience` | Separate Platform issuer/audience |
| `Email__Host`, `Email__Port` | SMTP host and port (1-65535) |
| `Email__From`, `Email__Username`, `Email__Password` | SMTP sender and credentials |
| `Seed__AdminPhone`, `Seed__AdminPassword` | Optional initial Director credentials; both required |
| `AllowedHosts` | Semicolon-separated accepted host names, for example `api.example.mn;localhost` (never `*`) |
| `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, ... | Exact browser origins (scheme, host and optional port) |
| `ForwardedHeaders__Enabled` | Set to `true` only behind a trusted reverse proxy |
| `ForwardedHeaders__KnownProxies__0`, ... | Trusted proxy IP addresses |
| `ForwardedHeaders__KnownNetworks__0`, ... | Trusted proxy CIDR networks |

JWT signing keys, issuers and audiences must differ between the Clinic and Platform
schemes. Startup validates them in every environment. SMTP is required outside the
isolated `Testing` environment. The disabled Platform email/SMS sender is separate
from the legacy SMTP service; it must be replaced before enabling Platform signup.

`appsettings.Example.json` is documentation only and is not automatically loaded.
Its values are placeholders, not credentials. Replace them through private
configuration, never by committing real values into the example.

The default `AllowedHosts` is `localhost`; override it when deploying behind a
real domain. CORS denies cross-origin browsers by default until permitted origins
are configured. It never grants credentialed wildcard access.

Forwarded headers are disabled by default. When enabled, configure at least one
trusted proxy IP or CIDR; unknown sources are never permitted to override the
client IP/scheme, and at most one forwarding hop is processed. Do **not** set
`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` as a shortcut: it may trust arbitrary
forwarded values. `UseForwardedHeaders` runs before HTTPS redirection and IP-based
rate limiting. HSTS is enabled outside Development; confirm TLS and your proxy's
own HSTS behavior during deployment.
