# Patient registration channel choice

New clients choose `verificationMethod: "sms" | "email"` in the registration
request. Provide the selected contact; the second contact is optional. The
server does not verify the unused contact. SMS accepts an 8-digit Mongolian
number, optionally preceded by the digits `976`. Only the verified contact
can be used for password login on new-flow accounts.

- POST `/api/Auth/register`: `fullName`, `phone`, `email`, `password`,
  `clinicId`, and `verificationMethod`. Returns an opaque
  `registrationToken`, `userId`, accepted `verificationMethod`, and
  `retryAfterSeconds`. Older email-only API clients remain compatible.
- POST `/api/Auth/registration-code`: `userId`, `registrationToken`,
  and `verificationMethod` for resend or changing channel. The alternate
  contact must have been supplied during signup. A resend or switch shares
  the same 60-second cooldown. After losing the session, submit the same
  pending registration details and original password to recover it.
- POST `/api/Auth/verify`: `userId`, `registrationToken`, and the six-digit
  `code`. Codes expire after 120 seconds and are single use. The existing
  IP limiter applies and durable per-account quotas span both channels.
  An accepted new send invalidates the previous accepted code; an
  unavailable/failed send does not activate the account. An API success
  confirms provider acceptance, not that a recipient opened or received it.

For real delivery, configure the server securely, never in the client,
  Git history, or chat:
- Email: `Email__Host`, `Email__Port`, `Email__From`, `Email__Username`, and
  `Email__Password` for authenticated STARTTLS SMTP.
- SMS: `Notifications__Sms__Enabled=true` and
  `Notifications__Sms__Endpoint` for the HTTPS gateway. Set
  `Notifications__Sms__Token` when the gateway requires a bearer token.
- A live multi-instance deployment needs a shared, access-controlled
  ASP.NET Core Data Protection key ring with an application discriminator
  and encryption at rest so pending tokens survive restarts and replicas.
  Provisioning and live provider verification are separate operator work.

The classroom runner uses its own in-memory SQLite and console-only
adapters for both channels, with `@example.invalid` email and `0000xxxx`
phone numbers. No real SMS or email is sent. Appointment create/reschedule
still require a verified email by existing independent policy; SMS
activation enables sign-in/profile, not booking without verified email.
