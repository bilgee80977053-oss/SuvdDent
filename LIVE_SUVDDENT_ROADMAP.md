# Suvd Dent — LIVE services, details and payment estimate (2026-11-16)

## Status: implementation work, NOT cleared for production

This branch is based on `feature/figma-clinical-premium-ui` and does not
modify `main`. It adds LIVE patient-facing service education, a protected
appointment-linked price estimate, preliminary estimate email, and director
service/tariff editing.

**DO NOT tell a patient that a payment has been made or a legal eBarimt has
been issued. There is currently no payment gateway or settlement ledger.**

### Brand and navigation

- Patient-facing name: **Suvd Dent**.
- Location copy: **Дархан-Уул • Suvd Dent**.
- **NEXORA** is a small platform mark, not the clinic name.
- Intended flow: Home → LIVE clinic service → educational detail and
  provisional clinic tariff → doctor/date/available slot → booking →
  My appointments → Payment details.
- The new bottom sheet uses the actual clinic service name, description,
  optional aftercare text and price from the authenticated API. The
  educational layer covers twelve common dentistry topics sourced from
  public patient-information guidance; it does not assert the clinic
  currently performs all twelve procedures.
- A patient can book **only a service published by the clinic's LIVE API**.

Clinical background:
https://www.nhs.uk/live-well/healthy-teeth-and-gums/dental-treatments/

### Price governance: verify the tariffs before opening to patients

The repository's original `DbSeeder` contains example figures for fillings,
extractions and whitening. They are not independently verified Suvd Dent
tariffs. Existing database rows may also contain historical test amounts.
**These numbers are not a valid basis for a production charge.**

The clinic's authenticated **Director** account can review and update its
own service catalog at:

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/admin/services` | Review current and inactive services |
| POST | `/api/admin/services` | Add an approved service and its tariff |
| PUT | `/api/admin/services/{id}` | Edit name, description, minimum/maximum price, aftercare |
| POST | `/api/admin/services/{id}/deactivate` | Stop new bookings without deleting history |
| POST | `/api/admin/services/{id}/activate` | Re-enable booking |

All endpoints derive `clinicId` from the Director's verified JWT. Price
changes do not currently record who changed them; add a tamper-resistant audit
trail and administrator UI before production rollout. Do not add unapproved
services to a real clinic's active booking list.

```json
{
  "name": "Шүдний үзлэг",
  "description": "Эмнэлгийн баталгаажуулсан үйлчилгээний тайлбар",
  "minPrice": 0,
  "maxPrice": null,
  "aftercareText": "Эмчийн баталгаажуулсан заавар"
}
```

`minPrice: 0` means **price not yet approved**, not free service. Enter the
clinic's signed/approved real tariff; never copy another clinic's rates.

### Protected preliminary price estimate

`GET /api/appointments/{id}/payment-estimate` — authenticated Patient only,
and ownership checked through matching clinic/patient/user relationships.

- Gets the current clinic service catalog minimum/maximum.
- Returns `isFinalAmount=false`, `totalDueMnt=null`,
  `paymentState=NotTracked`, `receiptState=NotIssued`.
- Does not invent fees, VAT, discounts, accepted payments or paid receipts.
- Does not persist financial records or perform transactions.
- The estimate is **NOT a snapshot of the charge at booking**.
- A future payment module must replace this with an immutable invoice/charge
  snapshot approved by the clinic and a separate settlement record.

`POST /api/appointments/{id}/payment-estimate/email` — **explicit** Patient
action, verified registered email only, auth rate-limited. Requires working
SMTP configuration. Sends a preliminary quote with the prominent label
**NOT A PAID RECEIPT**. Does not email a tax receipt.

The Flutter payment screen permits viewing six potential channels: QPay/QR,
card/POS, bank transfer, bank app, cash at clinic, corporate invoice.
Selecting one shows the technical/clinic requirements, not a fabricated QR,
merchant account, invoice or charge. Email/physical receipt preferences are
shown; actual paid receipt delivery is not yet enabled. The preliminary
estimate can be copied and the emailed preliminary quote can be requested.

Official payment-gateway docs:
https://developer.qpay.mn/mn/docs/merchant?version=2.0.0

### To complete **real** payment collection, still required

1. Signed clinic-approved live price list and service inclusions. Add a
   versioned, approved tariff and effective dates.
2. Real clinic/business identity, legal receipt issuer, tax/eBarimt rules,
   cash desk and card terminal arrangements.
3. QPay merchant account/credentials in **backend secret storage only**;
   backend invoice creation, provider payment check, verified callbacks,
   idempotency, timeouts, refunds and reconciliations. Never trust
   client-side "paid" claims.
4. Persistence migrations for immutable invoice line-items, amounts,
   adjustments, tax rules, tender, status history, receipts and refunds.
5. Email receipt outbox after successful verified settlement; email or
   downloadable/printable receipt. Printed document and tax receipt must be
   distinguishable.
6. Director/Cashier admin interface, audit log, payment test suite,
   accessibility checks, medical text review and security testing.
7. Production backend deployment with TLS, backups, privacy controls,
   tenant isolation, service catalogue validation, logging and monitoring.

### Windows: check out and verify

Use the **existing** `C:\Users\Surface\SuvdDent` checkout; it has local
untracked SQL work that must be preserved. Do not force-clean or reset.

```powershell
cd "C:\Users\Surface\SuvdDent"
git status --short
git fetch origin
git switch --track origin/feature/suvd-dent-live-catalog-billing
cd mobile
flutter pub get
flutter analyze
flutter test
```

Run the backend **only** against your authorized configured development DB;
startup can apply migrations:

```powershell
cd "C:\Users\Surface\SuvdDent"
dotnet build backend/NexoraDental.sln
dotnet test backend/NexoraDental.sln
dotnet run --project backend/DentalApi --launch-profile http
```

The backend profile uses `http://localhost:5018`. In another PowerShell
terminal, with an **authorized** clinic ID obtained from your development
director/admin record:

```powershell
cd "C:\Users\Surface\SuvdDent\mobile"
$clinicId = 1 # EXAMPLE ONLY — replace with your actual authorized clinic ID
flutter run -d chrome --web-hostname localhost --web-port 7357 --dart-define=NEXORA_API_BASE_URL=http://localhost:5018 --dart-define=NEXORA_CLINIC_ID=$clinicId
```

Configure the backend CORS list with exact origin
`http://localhost:7357`, without wildcard; avoid committing secrets.
Omitting the `NEXORA_API_BASE_URL` dart define still enters the historical
DEMO route from the existing app; the new payment and service workflows
require the LIVE settings.

### Verification limitations

Changes were made through the GitHub connector. No Flutter SDK, dotnet SDK,
database, merchant credentials or clinic-authorized LIVE API were available
to this assistant execution environment. Therefore **no build, Flutter test,
backend integration test, end-to-end payment or SMTP test has been run here**.
Do not merge/deploy before local checks and independent medical/billing review.
