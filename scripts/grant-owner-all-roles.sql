-- Grants one account every platform role (Learner, Instructor, Admin, SuperAdmin) and an
-- Approved instructor profile, so it can use the buyer, instructor and admin areas of the site.
--
-- Plain SQL: runs in DBeaver / pgAdmin / DataGrip (run as a script) and in psql (-f). No psql
-- meta-commands, no client-side variables. To use another email, change v_email below.
--
-- Idempotent: safe to run again. The DO block is a single statement, so it is atomic.
-- Requires the Identity + Catalog migrations to be applied (IDENTITY.USERS / USER_ROLES / ROLES /
-- SECURITY_AUDITS, CATALOG.INSTRUCTOR_PROFILES).
--
-- If the account has never signed in, it is created Active with a password hash that can never
-- verify. Signing in with Google (same email) links to this row; password sign-in works after
-- "forgot password". If the account already exists it is kept as is (only a Pending/Suspended
-- status is set to Active, and roles / instructor profile are added).
--
-- After running: sign out and sign in again - the access token (15 min) carries the old roles.

-- UUIDv7 for PostgreSQL < 18 (PG18 has uuidv7() built in). Lives in this session only (pg_temp).
CREATE OR REPLACE FUNCTION pg_temp.uuid_v7() RETURNS uuid AS $$
  SELECT encode(
    set_bit(
      set_bit(
        overlay(uuid_send(gen_random_uuid())
                placing substring(int8send(floor(extract(epoch FROM clock_timestamp()) * 1000)::bigint) FROM 3)
                FROM 1 FOR 6),
        52, 1),
      53, 1),
    'hex')::uuid
$$ LANGUAGE sql VOLATILE;

DO $$
DECLARE
  v_email   text := 'jatupolman048@gmail.com';   -- <<< the only value to edit
  v_norm    text := upper(v_email);
  v_user_id uuid;
BEGIN
  -- 1) The account: create when missing, otherwise make sure it can sign in.
  INSERT INTO "IDENTITY"."USERS"
    ("ID", "EMAIL", "NORMALIZED_EMAIL", "PASSWORD_HASH", "DISPLAY_NAME", "STATUS",
     "EMAIL_CONFIRMED_AT_UTC", "TWO_FACTOR_ENABLED", "CREATED_AT_UTC")
  VALUES
    (pg_temp.uuid_v7(), v_email, v_norm,
     md5(random()::text) || md5(random()::text),
     split_part(v_email, '@', 1), 'Active', now(), false, now())
  ON CONFLICT ("NORMALIZED_EMAIL") DO UPDATE
    SET "STATUS" = 'Active',
        "EMAIL_CONFIRMED_AT_UTC" = COALESCE("USERS"."EMAIL_CONFIRMED_AT_UTC", now()),
        "UPDATED_AT_UTC" = now()
    WHERE "USERS"."STATUS" IN ('PendingEmailConfirmation', 'Suspended');

  SELECT "ID" INTO v_user_id FROM "IDENTITY"."USERS" WHERE "NORMALIZED_EMAIL" = v_norm;

  -- 2) All four roles (Learner, Instructor, Admin, SuperAdmin).
  INSERT INTO "IDENTITY"."USER_ROLES" ("USER_ID", "ROLE_ID")
  SELECT v_user_id, r."ID" FROM "IDENTITY"."ROLES" r
  ON CONFLICT DO NOTHING;

  -- 3) Approved instructor profile (CreateCourse requires one even for Admin/SuperAdmin).
  INSERT INTO "CATALOG"."INSTRUCTOR_PROFILES"
    ("ID", "USER_ID", "DISPLAY_NAME", "HEADLINE", "BIO", "REVENUE_SHARE_PERCENT", "STATUS",
     "APPROVED_AT_UTC", "CREATED_AT_UTC")
  SELECT pg_temp.uuid_v7(), u."ID", u."DISPLAY_NAME", 'Owner account',
         'Platform owner account with all roles.', 70.00, 'Approved', now(), now()
  FROM "IDENTITY"."USERS" u
  WHERE u."ID" = v_user_id
  ON CONFLICT ("USER_ID") DO UPDATE
    SET "STATUS" = 'Approved',
        "APPROVED_AT_UTC" = COALESCE("INSTRUCTOR_PROFILES"."APPROVED_AT_UTC", now()),
        "UPDATED_AT_UTC" = now()
    WHERE "INSTRUCTOR_PROFILES"."STATUS" <> 'Approved';

  -- 4) Audit trail, same event type the admin "update roles" API writes.
  INSERT INTO "IDENTITY"."SECURITY_AUDITS" ("ID", "USER_ID", "EVENT_TYPE", "DETAIL", "OCCURRED_AT_UTC")
  VALUES (pg_temp.uuid_v7(), v_user_id, 'AdminUpdateUserRoles',
          'Roles set by script grant-owner-all-roles.sql to: Learner, Instructor, Admin, SuperAdmin; instructor profile approved',
          now());
END
$$;

-- Verify: expect one row, roles = Admin,Instructor,Learner,SuperAdmin, instructor_profile = Approved.
SELECT u."EMAIL", u."STATUS",
       string_agg(r."NAME", ',' ORDER BY r."NAME") AS roles,
       p."STATUS" AS instructor_profile
FROM "IDENTITY"."USERS" u
JOIN "IDENTITY"."USER_ROLES" ur ON ur."USER_ID" = u."ID"
JOIN "IDENTITY"."ROLES" r ON r."ID" = ur."ROLE_ID"
LEFT JOIN "CATALOG"."INSTRUCTOR_PROFILES" p ON p."USER_ID" = u."ID"
WHERE u."NORMALIZED_EMAIL" = upper('jatupolman048@gmail.com')
GROUP BY u."EMAIL", u."STATUS", p."STATUS";
