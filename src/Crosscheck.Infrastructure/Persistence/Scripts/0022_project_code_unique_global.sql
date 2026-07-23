-- Project codes are globally unique (reverses 0014). The Excel timesheet import
-- matches workbook job labels to projects by code alone — no client in hand — so a
-- code must identify exactly one project.
DO $$
DECLARE duplicate_codes text;
BEGIN
    SELECT string_agg(code, ', ') INTO duplicate_codes
    FROM (SELECT code FROM projects GROUP BY code HAVING count(*) > 1) d;
    IF duplicate_codes IS NOT NULL THEN
        RAISE EXCEPTION 'Cannot make project codes globally unique: duplicate codes exist (%). Rename them before migrating.', duplicate_codes;
    END IF;
END $$;

ALTER TABLE projects DROP CONSTRAINT ux_projects_client_code;
ALTER TABLE projects ADD CONSTRAINT ux_projects_code UNIQUE (code);
