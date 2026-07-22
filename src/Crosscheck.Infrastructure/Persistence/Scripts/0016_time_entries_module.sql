-- Attach time entries to a project module (0015). Entries created before a project had
-- modules keep module_id NULL — the "unassigned" bucket; once a project has live modules,
-- the service requires a module on new entries.

ALTER TABLE time_entries ADD COLUMN module_id uuid;

-- The module must belong to the entry's project (composite FK against
-- uq_project_modules_id_project).
ALTER TABLE time_entries ADD CONSTRAINT fk_time_entries_module_project
    FOREIGN KEY (module_id, project_id) REFERENCES project_modules (id, project_id);

-- The grid-cell key grows a module dimension: one entry per (person, project, module, day),
-- where "no module" is itself a bucket — hence NULLS NOT DISTINCT, which makes two
-- NULL-module rows for the same person/project/day collide exactly like the old constraint
-- did (so the swap is safe for all existing rows and the import dedupe rule still holds).
ALTER TABLE time_entries DROP CONSTRAINT uq_time_entries_person_project_date;
ALTER TABLE time_entries ADD CONSTRAINT uq_time_entries_person_project_module_date
    UNIQUE NULLS NOT DISTINCT (employee_id, project_id, module_id, entry_date);

CREATE INDEX ix_time_entries_module ON time_entries (module_id) WHERE module_id IS NOT NULL;
