# Platform account foundation

PlatformAccount is a global customer identity. It has no ClinicId or staff role.
Existing User/Patient/DoctorProfile, Clinic JWT authentication, legacy auth and
patient endpoints remain unchanged. No existing credentials or patient cards are
merged, copied or linked during registration or migration.

## API

All paths below start with /api/platform/auth/ and accept JSON.

- POST register: fullName, email and/or phone, password. Returns 202; account stays
  inactive until contact verification. Globally duplicated contacts return 409.
- POST verify: identifier, code. Confirms that specific contact, returns 200.
- POST resend-verification: identifier. Returns generic 202 for eligible/unknown
  identifiers; a 60-second cooldown applies to each contact/purpose.
- POST login: identifier, password. Returns token and own account DTO, or 401.
- GET me: requires the PlatformAccount policy and a Platform JWT. Ownership comes
  solely from validated accountId/sub, never a body/query/header account ID.
- POST forgot-password: identifier. Generic 202 for eligible/unknown accounts.
- POST reset-password: identifier, code, newPassword. Returns 200 and increments
  the account TokenVersion, invalidating all prior Platform tokens for that account.

Email is trimmed/lowercased, case-insensitively unique. Mongolian phone numbers
accept eight digits or +976 followed by eight digits and store the latter form.
At least one contact is required. If both are supplied, email verification is sent
first; use resend-verification with the phone to verify SMS separately. Login and
password recovery require the actual identifier's verified contact, not just a
verified different contact. Deactivated verified accounts cannot reactivate through
registration verification. No contact/profile editing endpoint is provided yet.

Passwords require at least 12 characters and at most 72 UTF-8 bytes; BCrypt cost 12
is used, with the byte limit checked before BCrypt to prevent truncation collisions.
Six-digit cryptographically generated codes are BCrypt hashed, purpose/channel/account
bound, expire after ten minutes, allow five failed attempts and are consumed once.
Reset consumes outstanding account challenges. Write transactions serialize code
verification and reset; unique DB indexes protect concurrent duplicate registrations.
SQLite busy/locked writes return 409. Auth actions have a separate 20/minute/IP
rate limit and never alter the existing Clinic auth rate-limit policy.
Recovery/resend responses avoid explicit account-existence disclosure; registration
returns 409 on duplicates. No constant-time enumeration guarantee is claimed.

## JWT separation and configuration

Platform scheme = Platform, policy = PlatformAccount. Tokens use a separate issuer,
audience, signing key, typ=nexora-platform+jwt and HS256. They contain sub, accountId,
identityType=platform and tokenVersion, without clinicId, userId, staff role or
contact information. The Platform handler validates the active, verified DB account
and current TokenVersion on every authenticated request. Clinic JWTs cannot satisfy
the Platform policy; Platform JWTs cannot pass the unchanged Clinic handler.

Configure PlatformJwt:Key (at least 32 UTF-8 bytes), PlatformJwt:Issuer and
PlatformJwt:Audience through private configuration. All three must differ from
legacy Jwt settings. No real keys are checked in. Tokens expire after 30 minutes.
Without valid settings Platform login fails closed (503); protected Platform
endpoints require that configuration before deployment. Legacy endpoints do not
require the named Platform scheme's options to be initialized.

IPlatformMessageSender is an injectable transport boundary. The default disabled
sender performs no network I/O and registration/challenge actions return 503 without
creating accounts when delivery is unavailable. A delivery exception after a
committed challenge returns 503, leaving a pending account/code for later resend;
it never auto-verifies or returns the code. No production SMTP/SMS transport has
been introduced. PlatformApiFactory injects a fake email/SMS sender which captures
messages only in memory for tests; application logs/responses never print codes,
passwords or signing keys. Configure a reviewed real transport in a separate change
before enabling production registration/recovery. Do not expose the fake provider
through an API or production dependency registration.

## ClinicPatientLink

This table provides schema only: no public linking, importing or booking API exists.
PlatformAccountId references the global PlatformAccount primary key. The composite
(ClinicId,PatientId) FK references Patient(ClinicId,Id), with Restrict. Unique indexes
permit one linked local card per account/clinic and prevent sharing one card between
accounts. Patient.UserId remains nullable, with its existing User tenant FK intact.
PlatformAccount has no ClinicId, so its FK is intentionally a single global key.
Future verified linking must prove card ownership; matching email/phone/name alone
must never link a legacy card. Clinical data remains inside each clinic's Patient.
No staff membership or global clinical-history sharing is introduced.

## Migration and validation

20261026131551_AddPlatformAccounts is additive: PlatformAccounts,
PlatformVerificationCodes, ClinicPatientLinks, their indexes/checks/FKs, and history.
No legacy table rebuild or legacy credential backfill occurs. The generated SQL is
AddPlatformAccounts.sql. It must follow the existing scheduling/location migrations.
Down deletes the new account/link/challenge data; production backups and deployment
approval remain necessary. Existing non-Testing startup auto-applies migrations:
do not start this changed API against a real database before deployment approval.

Tests run with real JWT middleware and synthetic SQLite in-memory data, including
separate concurrent request connections. Migration tests rehearse EF and generated
SQL against the prior schema, preserving old rows, schema, indexes and foreign keys.
The real dental.db, backups, SMTP/SMS and live API are never used by these tests.
