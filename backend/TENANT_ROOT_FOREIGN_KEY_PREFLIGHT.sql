-- Read-only preflight for Step 11.6.1 tenant-root foreign keys.
-- Confirm current_database/current_user/host/port before interpreting results.
-- Any returned row requires owner review; this script never repairs data.
BEGIN TRANSACTION READ ONLY;

SELECT current_database() AS database_name,
       current_user AS current_role,
       host(inet_server_addr()) AS server_host,
       inet_server_port() AS server_port,
       current_schema() AS current_schema;

WITH missing_clinic(issue, record_id, clinic_id, user_id) AS (
    SELECT 'Users.ClinicId has no Clinics row',
           u."Id", u."ClinicId", NULL::integer
    FROM public."Users" AS u
    LEFT JOIN public."Clinics" AS c ON c."Id" = u."ClinicId"
    WHERE c."Id" IS NULL
    UNION ALL
    SELECT 'Services.ClinicId has no Clinics row',
           s."Id", s."ClinicId", NULL::integer
    FROM public."Services" AS s
    LEFT JOIN public."Clinics" AS c ON c."Id" = s."ClinicId"
    WHERE c."Id" IS NULL
    UNION ALL
    SELECT 'Patients.ClinicId has no Clinics row',
           p."Id", p."ClinicId", p."UserId"
    FROM public."Patients" AS p
    LEFT JOIN public."Clinics" AS c ON c."Id" = p."ClinicId"
    WHERE c."Id" IS NULL
)
SELECT issue, record_id, clinic_id, user_id
FROM missing_clinic
ORDER BY issue, clinic_id, record_id;

ROLLBACK;
