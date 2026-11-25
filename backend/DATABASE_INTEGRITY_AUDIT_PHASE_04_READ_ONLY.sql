-- SuvdDent Phase 04 PostgreSQL integrity audit.
-- READ ONLY. Run only after separately confirming the approved database/role.
-- No DDL, DML, migration, sequence advancement, or credential query is present.
BEGIN TRANSACTION READ ONLY;

-- Q01: Target identity. Stop if this is not the approved target.
SELECT current_database() AS database_name,
       current_user AS current_role,
       host(inet_server_addr()) AS server_host,
       inet_server_port() AS server_port,
       current_schema() AS current_schema,
       current_setting('server_version') AS server_version;

-- Q02: Expected application tables vs all public base/partitioned tables.
WITH expected(table_name) AS (
    VALUES
        ('Appointments'), ('ClinicPatientLinks'), ('Clinics'), ('DoctorProfiles'),
        ('DoctorServices'), ('DoctorWorkingHours'), ('NotificationOutbox'),
        ('NotificationPreference'), ('PatientAvailabilities'),
        ('PatientAvailabilitySchedules'), ('Patients'),
        ('PhoneVerificationChallenge'), ('PlatformAccounts'),
        ('PlatformVerificationCodes'), ('Services'), ('Users'), ('VerificationCodes')
),
actual(table_name) AS (
    SELECT c.relname
    FROM pg_catalog.pg_class AS c
    JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
    WHERE n.nspname = 'public'
      AND c.relkind IN ('r', 'p')
      AND c.relname <> '__EFMigrationsHistory'
)
SELECT COALESCE(e.table_name, a.table_name) AS table_name,
       CASE
           WHEN e.table_name IS NULL THEN 'UNEXPECTED'
           WHEN a.table_name IS NULL THEN 'MISSING'
           ELSE 'EXPECTED'
       END AS inventory_status
FROM expected AS e
FULL OUTER JOIN actual AS a USING (table_name)
ORDER BY table_name;

-- Q02b: Row counts for every expected application table and their total.
-- Compare named counts to the supplied environment inventory; this is a live
-- observation only when an operator later runs the script against the target.
WITH table_counts(table_name, row_count) AS (
    SELECT 'Appointments', count(*)::bigint FROM public."Appointments"
    UNION ALL SELECT 'ClinicPatientLinks', count(*)::bigint FROM public."ClinicPatientLinks"
    UNION ALL SELECT 'Clinics', count(*)::bigint FROM public."Clinics"
    UNION ALL SELECT 'DoctorProfiles', count(*)::bigint FROM public."DoctorProfiles"
    UNION ALL SELECT 'DoctorServices', count(*)::bigint FROM public."DoctorServices"
    UNION ALL SELECT 'DoctorWorkingHours', count(*)::bigint FROM public."DoctorWorkingHours"
    UNION ALL SELECT 'NotificationOutbox', count(*)::bigint FROM public."NotificationOutbox"
    UNION ALL SELECT 'NotificationPreference', count(*)::bigint FROM public."NotificationPreference"
    UNION ALL SELECT 'PatientAvailabilities', count(*)::bigint FROM public."PatientAvailabilities"
    UNION ALL SELECT 'PatientAvailabilitySchedules', count(*)::bigint FROM public."PatientAvailabilitySchedules"
    UNION ALL SELECT 'Patients', count(*)::bigint FROM public."Patients"
    UNION ALL SELECT 'PhoneVerificationChallenge', count(*)::bigint FROM public."PhoneVerificationChallenge"
    UNION ALL SELECT 'PlatformAccounts', count(*)::bigint FROM public."PlatformAccounts"
    UNION ALL SELECT 'PlatformVerificationCodes', count(*)::bigint FROM public."PlatformVerificationCodes"
    UNION ALL SELECT 'Services', count(*)::bigint FROM public."Services"
    UNION ALL SELECT 'Users', count(*)::bigint FROM public."Users"
    UNION ALL SELECT 'VerificationCodes', count(*)::bigint FROM public."VerificationCodes"
)
SELECT table_name, row_count FROM table_counts
UNION ALL
SELECT 'TOTAL_APPLICATION_ROWS', sum(row_count) FROM table_counts
ORDER BY table_name;

-- Q03: Applied PostgreSQL migration history; exactly five IDs are expected.
WITH expected(migration_id, ordinal, product_version) AS (
    VALUES
        ('20261027132306_PostgreSqlBaseline', 1, '10.0.13'),
        ('20261027180725_AddPostgreSqlAppointmentFoundation', 2, '10.0.13'),
        ('20261102165221_AddPostgreSqlNotificationOutbox', 3, '10.0.13'),
        ('20261102180315_AddPostgreSqlPhoneVerificationChallenges', 4, '10.0.13'),
        ('20261125115303_AddPostgreSqlClinicTenantRootForeignKeys', 5, '10.0.13')
),
actual AS (
    SELECT "MigrationId" AS migration_id,
           "ProductVersion" AS product_version,
           row_number() OVER (ORDER BY "MigrationId") AS ordinal
    FROM public."__EFMigrationsHistory"
)
SELECT COALESCE(e.migration_id, a.migration_id) AS migration_id,
       e.ordinal AS expected_ordinal,
       a.ordinal AS applied_ordinal,
       e.product_version AS expected_product_version,
       a.product_version AS applied_product_version,
       CASE
           WHEN e.migration_id IS NULL THEN 'UNEXPECTED'
           WHEN a.migration_id IS NULL THEN 'MISSING'
           WHEN e.ordinal IS DISTINCT FROM a.ordinal THEN 'ORDER_MISMATCH'
           WHEN e.product_version IS DISTINCT FROM a.product_version THEN 'PRODUCT_VERSION_MISMATCH'
           ELSE 'EXPECTED'
       END AS history_status
FROM expected AS e
FULL OUTER JOIN actual AS a USING (migration_id)
ORDER BY COALESCE(e.ordinal, a.ordinal), migration_id;

-- Q04: Actual public-schema columns, nullability, defaults, and identity generation.
-- Compare this result with PostgreSqlAppDbContextModelSnapshot.cs.
SELECT table_name, ordinal_position, column_name, data_type, udt_name,
       is_nullable, column_default, is_identity, identity_generation
FROM information_schema.columns
WHERE table_schema = 'public'
ORDER BY table_name, ordinal_position;

-- Q05: Actual constraints, validation state, and referential actions.
SELECT tbl.relname AS table_name,
       con.conname AS constraint_name,
       CASE con.contype
           WHEN 'p' THEN 'PRIMARY KEY'
           WHEN 'u' THEN 'UNIQUE'
           WHEN 'f' THEN 'FOREIGN KEY'
           WHEN 'c' THEN 'CHECK'
           WHEN 'x' THEN 'EXCLUSION'
           ELSE con.contype::text
       END AS constraint_type,
       pg_get_constraintdef(con.oid, true) AS definition,
       con.convalidated AS is_validated,
       con.condeferrable AS is_deferrable,
       con.condeferred AS initially_deferred
FROM pg_catalog.pg_constraint AS con
JOIN pg_catalog.pg_class AS tbl ON tbl.oid = con.conrelid
JOIN pg_catalog.pg_namespace AS ns ON ns.oid = tbl.relnamespace
WHERE ns.nspname = 'public'
ORDER BY tbl.relname, constraint_type, con.conname;

-- Q06: Index definitions and available usage counters.
-- Low scan counts are leads only; statistics may have been reset.
SELECT tbl.relname AS table_name,
       idx.relname AS index_name,
       am.amname AS access_method,
       pi.indisprimary AS is_primary,
       pi.indisunique AS is_unique,
       pi.indisvalid AS is_valid,
       pi.indisready AS is_ready,
       pg_get_indexdef(pi.indexrelid) AS definition,
       COALESCE(stats.idx_scan, 0) AS scans_since_stats_reset,
       COALESCE(stats.idx_tup_read, 0) AS tuples_read_since_stats_reset,
       COALESCE(stats.idx_tup_fetch, 0) AS tuples_fetched_since_stats_reset
FROM pg_catalog.pg_index AS pi
JOIN pg_catalog.pg_class AS tbl ON tbl.oid = pi.indrelid
JOIN pg_catalog.pg_namespace AS ns ON ns.oid = tbl.relnamespace
JOIN pg_catalog.pg_class AS idx ON idx.oid = pi.indexrelid
JOIN pg_catalog.pg_am AS am ON am.oid = idx.relam
LEFT JOIN pg_catalog.pg_stat_user_indexes AS stats
       ON stats.relid = pi.indrelid AND stats.indexrelid = pi.indexrelid
WHERE ns.nspname = 'public'
ORDER BY tbl.relname, idx.relname;

-- Q07: Root-clinic orphans and same-clinic relationship checks.
-- No rows are expected. Includes the appointment-to-doctor-service pair,
-- which is not currently a declared FK in the inspected EF model.
WITH violations(issue, record_id, clinic_id, related_id) AS (
    SELECT 'Users.ClinicId has no Clinic', u."Id"::text, u."ClinicId"::text, NULL::text
    FROM public."Users" AS u
    LEFT JOIN public."Clinics" AS c ON c."Id" = u."ClinicId"
    WHERE c."Id" IS NULL
    UNION ALL
    SELECT 'Services.ClinicId has no Clinic', s."Id"::text, s."ClinicId"::text, NULL::text
    FROM public."Services" AS s
    LEFT JOIN public."Clinics" AS c ON c."Id" = s."ClinicId"
    WHERE c."Id" IS NULL
    UNION ALL
    SELECT 'Patient user missing or cross-clinic', p."Id"::text, p."ClinicId"::text, p."UserId"::text
    FROM public."Patients" AS p
    LEFT JOIN public."Users" AS u
      ON u."ClinicId" = p."ClinicId" AND u."Id" = p."UserId"
    WHERE p."UserId" IS NOT NULL AND u."Id" IS NULL
    UNION ALL
    SELECT 'Doctor profile user missing or cross-clinic', d."Id"::text, d."ClinicId"::text, d."UserId"::text
    FROM public."DoctorProfiles" AS d
    LEFT JOIN public."Users" AS u
      ON u."ClinicId" = d."ClinicId" AND u."Id" = d."UserId"
    WHERE u."Id" IS NULL
    UNION ALL
    SELECT 'Doctor service doctor missing or cross-clinic', ds."Id"::text, ds."ClinicId"::text, ds."DoctorProfileId"::text
    FROM public."DoctorServices" AS ds
    LEFT JOIN public."DoctorProfiles" AS d
      ON d."ClinicId" = ds."ClinicId" AND d."Id" = ds."DoctorProfileId"
    WHERE d."Id" IS NULL
    UNION ALL
    SELECT 'Doctor service service missing or cross-clinic', ds."Id"::text, ds."ClinicId"::text, ds."ServiceId"::text
    FROM public."DoctorServices" AS ds
    LEFT JOIN public."Services" AS s
      ON s."ClinicId" = ds."ClinicId" AND s."Id" = ds."ServiceId"
    WHERE s."Id" IS NULL
    UNION ALL
    SELECT 'Doctor working-hours profile missing or cross-clinic', h."Id"::text, h."ClinicId"::text, h."DoctorProfileId"::text
    FROM public."DoctorWorkingHours" AS h
    LEFT JOIN public."DoctorProfiles" AS d
      ON d."ClinicId" = h."ClinicId" AND d."Id" = h."DoctorProfileId"
    WHERE d."Id" IS NULL
    UNION ALL
    SELECT 'Patient availability schedule patient missing or cross-clinic',
           s."PatientId"::text, s."ClinicId"::text, s."PatientId"::text
    FROM public."PatientAvailabilitySchedules" AS s
    LEFT JOIN public."Patients" AS p
      ON p."ClinicId" = s."ClinicId" AND p."Id" = s."PatientId"
    WHERE p."Id" IS NULL
    UNION ALL
    SELECT 'Patient availability schedule missing or cross-clinic',
           a."Id"::text, a."ClinicId"::text, a."PatientId"::text
    FROM public."PatientAvailabilities" AS a
    LEFT JOIN public."PatientAvailabilitySchedules" AS s
      ON s."ClinicId" = a."ClinicId" AND s."PatientId" = a."PatientId"
    WHERE s."PatientId" IS NULL
    UNION ALL
    SELECT 'Appointment patient missing or cross-clinic', a."Id"::text, a."ClinicId"::text, a."PatientId"::text
    FROM public."Appointments" AS a
    LEFT JOIN public."Patients" AS p
      ON p."ClinicId" = a."ClinicId" AND p."Id" = a."PatientId"
    WHERE p."Id" IS NULL
    UNION ALL
    SELECT 'Appointment doctor missing or cross-clinic', a."Id"::text, a."ClinicId"::text, a."DoctorProfileId"::text
    FROM public."Appointments" AS a
    LEFT JOIN public."DoctorProfiles" AS d
      ON d."ClinicId" = a."ClinicId" AND d."Id" = a."DoctorProfileId"
    WHERE d."Id" IS NULL
    UNION ALL
    SELECT 'Appointment service missing or cross-clinic', a."Id"::text, a."ClinicId"::text, a."ServiceId"::text
    FROM public."Appointments" AS a
    LEFT JOIN public."Services" AS s
      ON s."ClinicId" = a."ClinicId" AND s."Id" = a."ServiceId"
    WHERE s."Id" IS NULL
    UNION ALL
    SELECT 'Appointment has no doctor-service mapping', a."Id"::text, a."ClinicId"::text, a."DoctorProfileId"::text
    FROM public."Appointments" AS a
    LEFT JOIN public."DoctorServices" AS ds
      ON ds."ClinicId" = a."ClinicId"
     AND ds."DoctorProfileId" = a."DoctorProfileId"
     AND ds."ServiceId" = a."ServiceId"
    WHERE ds."Id" IS NULL
    UNION ALL
    SELECT 'Clinic-patient link patient missing or cross-clinic', l."Id"::text, l."ClinicId"::text, l."PatientId"::text
    FROM public."ClinicPatientLinks" AS l
    LEFT JOIN public."Patients" AS p
      ON p."ClinicId" = l."ClinicId" AND p."Id" = l."PatientId"
    WHERE p."Id" IS NULL
    UNION ALL
    SELECT 'Clinic-patient link platform account missing', l."Id"::text, l."ClinicId"::text, l."PlatformAccountId"::text
    FROM public."ClinicPatientLinks" AS l
    LEFT JOIN public."PlatformAccounts" AS a ON a."Id" = l."PlatformAccountId"
    WHERE a."Id" IS NULL
    UNION ALL
    SELECT 'Platform verification code account missing', c."Id"::text, NULL::text, c."PlatformAccountId"::text
    FROM public."PlatformVerificationCodes" AS c
    LEFT JOIN public."PlatformAccounts" AS a ON a."Id" = c."PlatformAccountId"
    WHERE a."Id" IS NULL
    UNION ALL
    SELECT 'Verification code user missing', c."Id"::text, u."ClinicId"::text, c."UserId"::text
    FROM public."VerificationCodes" AS c
    LEFT JOIN public."Users" AS u ON u."Id" = c."UserId"
    WHERE u."Id" IS NULL
    UNION ALL
    SELECT 'Notification preference user missing or cross-clinic',
           p."UserId"::text, p."ClinicId"::text, p."UserId"::text
    FROM public."NotificationPreference" AS p
    LEFT JOIN public."Users" AS u
      ON u."ClinicId" = p."ClinicId" AND u."Id" = p."UserId"
    WHERE u."Id" IS NULL
    UNION ALL
    SELECT 'Notification outbox appointment missing or cross-clinic',
           o."Id"::text, o."ClinicId"::text, o."AppointmentId"::text
    FROM public."NotificationOutbox" AS o
    LEFT JOIN public."Appointments" AS a
      ON a."ClinicId" = o."ClinicId" AND a."Id" = o."AppointmentId"
    WHERE a."Id" IS NULL
    UNION ALL
    SELECT 'Phone challenge user missing or cross-clinic',
           p."Id"::text, p."ClinicId"::text, p."UserId"::text
    FROM public."PhoneVerificationChallenge" AS p
    LEFT JOIN public."Users" AS u
      ON u."ClinicId" = p."ClinicId" AND u."Id" = p."UserId"
    WHERE u."Id" IS NULL
    UNION ALL
    SELECT 'Verified clinic verifier missing or cross-clinic',
           c."Id"::text, c."Id"::text, c."TimeZoneVerifiedByUserId"::text
    FROM public."Clinics" AS c
    LEFT JOIN public."Users" AS u
      ON u."ClinicId" = c."TimeZoneVerifiedClinicId"
     AND u."Id" = c."TimeZoneVerifiedByUserId"
    WHERE c."TimeZoneStatus" = 'Verified' AND u."Id" IS NULL
)
SELECT issue, record_id, clinic_id, related_id
FROM violations
ORDER BY issue, clinic_id, record_id;

-- Q08: Case-folded duplicates. Values are not emitted; IDs are enough to investigate.
SELECT 'Service name' AS identity_type,
       "ClinicId" AS clinic_id,
       array_agg("Id" ORDER BY "Id") AS record_ids,
       count(*) AS duplicate_count
FROM public."Services"
GROUP BY "ClinicId", lower(btrim("Name"))
HAVING count(*) > 1
UNION ALL
SELECT 'User email' AS identity_type,
       "ClinicId" AS clinic_id,
       array_agg("Id" ORDER BY "Id") AS record_ids,
       count(*) AS duplicate_count
FROM public."Users"
WHERE "Email" IS NOT NULL
GROUP BY "ClinicId", lower(btrim("Email"))
HAVING count(*) > 1
ORDER BY identity_type, clinic_id;

-- Q09: Invalid domain/range rows. No rows are expected.
WITH violations(issue, record_id, clinic_id) AS (
    SELECT 'Unknown user role', u."Id"::text, u."ClinicId"::text
    FROM public."Users" AS u
    WHERE u."Role" NOT IN ('Patient', 'Doctor', 'Nurse', 'Accountant', 'Director')
    UNION ALL
    SELECT 'Invalid service price range', s."Id"::text, s."ClinicId"::text
    FROM public."Services" AS s
    WHERE s."MinPrice" < 0 OR s."MinPrice" > 1000000000
       OR (s."MaxPrice" IS NOT NULL AND
           (s."MaxPrice" < s."MinPrice" OR s."MaxPrice" > 1000000000))
    UNION ALL
    SELECT 'Invalid appointment time/status/request snapshot', a."Id"::text, a."ClinicId"::text
    FROM public."Appointments" AS a
    WHERE a."Status" NOT IN ('Confirmed', 'Cancelled')
       OR a."RequestId" = '00000000-0000-0000-0000-000000000000'::uuid
       OR NOT isfinite(a."StartUtc") OR NOT isfinite(a."EndUtc")
       OR NOT isfinite(a."OccupiedStartUtc") OR NOT isfinite(a."OccupiedEndUtc")
       OR NOT isfinite(a."CreatedAtUtc")
       OR a."OccupiedStartUtc" > a."StartUtc"
       OR a."StartUtc" >= a."EndUtc"
       OR a."EndUtc" > a."OccupiedEndUtc"
       OR a."DurationMinutesSnapshot" NOT BETWEEN 1 AND 1440
       OR btrim(a."ClinicTimeZoneIdSnapshot") = ''
)
SELECT issue, record_id, clinic_id
FROM violations
ORDER BY issue, clinic_id, record_id;

-- Q10: Overlapping confirmed appointment pairs using half-open intervals.
-- A row is a conflict to review; cancelled rows are intentionally excluded.
SELECT a."Id" AS first_appointment_id,
       b."Id" AS second_appointment_id,
       a."ClinicId" AS clinic_id,
       CASE
           WHEN a."DoctorProfileId" = b."DoctorProfileId"
            AND tstzrange(a."OccupiedStartUtc", a."OccupiedEndUtc", '[)') &&
                tstzrange(b."OccupiedStartUtc", b."OccupiedEndUtc", '[)')
           THEN 'DOCTOR_OCCUPIED_INTERVAL'
           ELSE 'PATIENT_TREATMENT_INTERVAL'
       END AS conflict_type
FROM public."Appointments" AS a
JOIN public."Appointments" AS b
  ON b."ClinicId" = a."ClinicId" AND b."Id" > a."Id"
WHERE a."Status" = 'Confirmed'
  AND b."Status" = 'Confirmed'
  AND (
      (a."DoctorProfileId" = b."DoctorProfileId"
       AND tstzrange(a."OccupiedStartUtc", a."OccupiedEndUtc", '[)') &&
           tstzrange(b."OccupiedStartUtc", b."OccupiedEndUtc", '[)'))
      OR
      (a."PatientId" = b."PatientId"
       AND tstzrange(a."StartUtc", a."EndUtc", '[)') &&
           tstzrange(b."StartUtc", b."EndUtc", '[)'))
  )
ORDER BY clinic_id, first_appointment_id, second_appointment_id;

-- Q11: Idempotency keys must be nonzero and unique per clinic/patient.
SELECT "ClinicId" AS clinic_id,
       "PatientId" AS patient_id,
       "RequestId" AS request_id,
       count(*) AS row_count,
       array_agg("Id" ORDER BY "Id") AS appointment_ids
FROM public."Appointments"
GROUP BY "ClinicId", "PatientId", "RequestId"
HAVING "RequestId" = '00000000-0000-0000-0000-000000000000'::uuid
    OR count(*) > 1
ORDER BY clinic_id, patient_id;

-- Q12a: Invalid weekly-hours intervals or overlapping intervals per doctor/day.
SELECT a."ClinicId" AS clinic_id,
       a."DoctorProfileId" AS doctor_profile_id,
       a."DayOfWeek" AS day_of_week,
       a."Id" AS first_row_id,
       b."Id" AS second_row_id,
       'OVERLAPPING_WEEKLY_HOURS' AS issue
FROM public."DoctorWorkingHours" AS a
JOIN public."DoctorWorkingHours" AS b
  ON b."ClinicId" = a."ClinicId"
 AND b."DoctorProfileId" = a."DoctorProfileId"
 AND b."DayOfWeek" = a."DayOfWeek"
 AND b."Id" > a."Id"
 AND a."StartMinute" < b."EndMinute"
 AND b."StartMinute" < a."EndMinute"
UNION ALL
SELECT h."ClinicId", h."DoctorProfileId", h."DayOfWeek", h."Id", NULL::integer,
       'INVALID_WEEKLY_HOURS'
FROM public."DoctorWorkingHours" AS h
WHERE h."DayOfWeek" NOT BETWEEN 0 AND 6
   OR h."StartMinute" < 0 OR h."StartMinute" >= h."EndMinute"
   OR h."EndMinute" > 1440
ORDER BY clinic_id, doctor_profile_id, day_of_week, first_row_id;

-- Q12b: Patient availability interval validity and overlaps (UTC tick values).
SELECT a."ClinicId" AS clinic_id,
       a."PatientId" AS patient_id,
       a."Id" AS first_row_id,
       b."Id" AS second_row_id,
       'OVERLAPPING_AVAILABILITY' AS issue
FROM public."PatientAvailabilities" AS a
JOIN public."PatientAvailabilities" AS b
  ON b."ClinicId" = a."ClinicId"
 AND b."PatientId" = a."PatientId"
 AND b."Id" > a."Id"
 AND a."StartUtc" < b."EndUtc"
 AND b."StartUtc" < a."EndUtc"
UNION ALL
SELECT v."ClinicId", v."PatientId", v."Id", NULL::integer,
       'INVALID_AVAILABILITY_INTERVAL'
FROM public."PatientAvailabilities" AS v
WHERE v."StartUtc" < 0 OR v."StartUtc" >= v."EndUtc"
   OR v."EndUtc" > 3155378975999999999
ORDER BY clinic_id, patient_id, first_row_id;

-- Q13: Outbox lifecycle/tick anomalies and verification-code domain anomalies.
-- Recipient, body, code hashes, and contact data are deliberately not selected.
WITH violations(issue, record_id, clinic_id) AS (
    SELECT 'Outbox invalid attempts/ticks/state timestamp', o."Id"::text, o."ClinicId"::text
    FROM public."NotificationOutbox" AS o
    WHERE o."Attempts" NOT BETWEEN 0 AND 8
       OR o."DueUtcTicks" < 0 OR o."NextAttemptUtcTicks" < 0
       OR (o."SentUtcTicks" IS NOT NULL AND o."SentUtcTicks" < 0)
       OR (o."State" = 'Sent' AND o."SentUtcTicks" IS NULL)
       OR (o."State" <> 'Sent' AND o."SentUtcTicks" IS NOT NULL)
       OR (o."Event" = 'Reminder' AND o."Channel" <> 'Sms')
    UNION ALL
    SELECT 'Platform verification code invalid domain/time/attempts',
           c."Id"::text, NULL::text
    FROM public."PlatformVerificationCodes" AS c
    WHERE c."Channel" NOT IN ('email', 'sms')
       OR c."Purpose" NOT IN ('registration', 'password-reset')
       OR c."ExpiresAt" <= c."CreatedAt"
       OR c."FailedAttempts" < 0
    UNION ALL
    SELECT 'Legacy verification code invalid channel/time/attempts',
           c."Id"::text, u."ClinicId"::text
    FROM public."VerificationCodes" AS c
    JOIN public."Users" AS u ON u."Id" = c."UserId"
    WHERE c."Channel" NOT IN ('email', 'sms')
       OR c."Purpose" NOT IN ('registration', 'password-reset', 'doctor-invite')
       OR c."ExpiresAt" <= c."CreatedAt"
       OR c."FailedAttempts" < 0
)
SELECT issue, record_id, clinic_id
FROM violations
ORDER BY issue, clinic_id, record_id;

-- Q14a: Identity columns and owned sequence metadata.
SELECT c.table_name, c.column_name, c.is_identity, c.identity_generation,
       pg_get_serial_sequence(format('%I.%I', c.table_schema, c.table_name), c.column_name)
           AS owned_sequence
FROM information_schema.columns AS c
WHERE c.table_schema = 'public'
  AND c.is_identity = 'YES'
ORDER BY c.table_name, c.ordinal_position;

-- Q14b: Sequence state visible to this role. NULL last_value may mean never used
-- or insufficient sequence privilege; do not infer drift from NULL.
SELECT schemaname, sequencename, start_value, min_value, max_value,
       increment_by, cycle, cache_size, last_value
FROM pg_catalog.pg_sequences
WHERE schemaname = 'public'
ORDER BY sequencename;

-- Q14c: Generates read-only SELECT statements to compare MAX(Id) to each
-- identity sequence. Review/execute generated SELECTs separately if permitted.
SELECT format(
           'SELECT %L AS table_name, COALESCE(MAX(%I), 0)::bigint AS max_id, ' ||
           '(SELECT last_value FROM %s) AS sequence_last_value, ' ||
           '(SELECT is_called FROM %s) AS sequence_is_called FROM %I.%I;',
           c.table_schema || '.' || c.table_name,
           c.column_name,
           pg_get_serial_sequence(format('%I.%I', c.table_schema, c.table_name), c.column_name),
           pg_get_serial_sequence(format('%I.%I', c.table_schema, c.table_name), c.column_name),
           c.table_schema,
           c.table_name
       ) AS read_only_sequence_comparison
FROM information_schema.columns AS c
WHERE c.table_schema = 'public'
  AND c.is_identity = 'YES'
  AND c.data_type = 'integer'
ORDER BY c.table_name;

-- Q15a: Current role flags and effective database/schema privileges.
SELECT r.rolname,
       r.rolsuper,
       r.rolcreatedb,
       r.rolcreaterole,
       r.rolreplication,
       r.rolbypassrls,
       has_database_privilege(current_user, current_database(), 'CONNECT') AS can_connect,
       has_database_privilege(current_user, current_database(), 'CREATE') AS can_create_schema,
       has_database_privilege(current_user, current_database(), 'TEMPORARY') AS can_create_temp,
       has_schema_privilege(current_user, 'public', 'USAGE') AS public_schema_usage,
       has_schema_privilege(current_user, 'public', 'CREATE') AS public_schema_create
FROM pg_catalog.pg_roles AS r
WHERE r.rolname = current_user;

-- Q15a2: Role memberships reachable from the current login. Review inherited
-- administrative flags as well as the current role's direct flags above.
SELECT granted_role.rolname AS inherited_role,
       granted_role.rolsuper,
       granted_role.rolcreatedb,
       granted_role.rolcreaterole,
       granted_role.rolreplication,
       granted_role.rolbypassrls
FROM pg_catalog.pg_roles AS granted_role
WHERE pg_has_role(current_user, granted_role.oid, 'MEMBER')
ORDER BY granted_role.rolname;

-- Q15b: Effective table privileges/ownership and row-level-security flags.
SELECT c.relname AS table_name,
       pg_get_userbyid(c.relowner) AS owner_role,
       has_table_privilege(current_user, c.oid, 'SELECT') AS can_select,
       has_table_privilege(current_user, c.oid, 'INSERT') AS can_insert,
       has_table_privilege(current_user, c.oid, 'UPDATE') AS can_update,
       has_table_privilege(current_user, c.oid, 'DELETE') AS can_delete,
       has_table_privilege(current_user, c.oid, 'TRUNCATE') AS can_truncate,
       c.relrowsecurity AS row_security_enabled,
       c.relforcerowsecurity AS row_security_forced
FROM pg_catalog.pg_class AS c
JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
WHERE n.nspname = 'public'
  AND c.relkind IN ('r', 'p')
ORDER BY c.relname;

-- Q15c: Effective sequence privileges; USAGE permits nextval(), UPDATE is
-- relevant to setval(), and SELECT permits reading sequence state.
SELECT c.relname AS sequence_name,
       has_sequence_privilege(current_user, c.oid, 'USAGE') AS can_use,
       has_sequence_privilege(current_user, c.oid, 'SELECT') AS can_select_state,
       has_sequence_privilege(current_user, c.oid, 'UPDATE') AS can_set_value
FROM pg_catalog.pg_class AS c
JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
WHERE n.nspname = 'public'
  AND c.relkind = 'S'
ORDER BY c.relname;

-- Q15d: Explicit ALTER DEFAULT PRIVILEGES overrides for this database.
-- No rows means PostgreSQL defaults apply; it does not mean no inherited grants.
SELECT COALESCE(n.nspname, '<all schemas>') AS schema_name,
       pg_get_userbyid(d.defaclrole) AS object_owner_role,
       CASE d.defaclobjtype
           WHEN 'r' THEN 'tables'
           WHEN 'S' THEN 'sequences'
           WHEN 'f' THEN 'functions'
           WHEN 'T' THEN 'types'
           WHEN 'n' THEN 'schemas'
           ELSE d.defaclobjtype::text
       END AS object_type,
       CASE acl.grantee
           WHEN 0 THEN 'PUBLIC'
           ELSE pg_get_userbyid(acl.grantee)
       END AS grantee,
       acl.privilege_type,
       acl.is_grantable
FROM pg_catalog.pg_default_acl AS d
LEFT JOIN pg_catalog.pg_namespace AS n ON n.oid = d.defaclnamespace
CROSS JOIN LATERAL aclexplode(d.defaclacl) AS acl
ORDER BY schema_name, object_owner_role, object_type, grantee, acl.privilege_type;

ROLLBACK;
