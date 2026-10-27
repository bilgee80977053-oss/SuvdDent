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

JWT signing keys, issuers and audiences must differ between the Clinic and Platform
schemes. Startup validates them in every environment. SMTP is required outside the
isolated `Testing` environment. The disabled Platform email/SMS sender is separate
from the legacy SMTP service; it must be replaced before enabling Platform signup.

`appsettings.Example.json` is documentation only and is not automatically loaded.
Its values are placeholders, not credentials. Replace them through private
configuration, never by committing real values into the example.
