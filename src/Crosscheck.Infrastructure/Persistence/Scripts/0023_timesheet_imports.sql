-- Excel timesheet import (design-doc.md §6.6). An upload is parsed into staging rows the
-- importer reviews and edits (project mapping, hours, description, billing role, include)
-- before committing; committed rows become approved time_entries tagged with import_id so
-- the whole import can be rolled back until any of its entries is invoiced.

CREATE TABLE timesheet_imports (
    id             uuid PRIMARY KEY,

    -- The person the workbook belongs to (matched/created by tenant email at upload).
    employee_id    uuid NOT NULL REFERENCES employees (id),

    file_name      text NOT NULL,
    year           int NOT NULL,

    -- pending (under review) → committed; rolled_back reverses a commit. A pending
    -- import can be discarded (hard-deleted with its staging rows — no entries exist yet).
    status         text NOT NULL DEFAULT 'pending'
                   CHECK (status IN ('pending', 'committed', 'rolled_back')),

    -- Parser oddities worth showing on review (skipped artifacts, calendar/description
    -- total mismatches). Newline-separated; null when the parse was clean.
    parse_warnings text,

    uploaded_by    uuid NOT NULL REFERENCES employees (id),
    uploaded_at    timestamptz NOT NULL DEFAULT now(),
    committed_by   uuid REFERENCES employees (id),
    committed_at   timestamptz,
    rolled_back_by uuid REFERENCES employees (id),
    rolled_back_at timestamptz
);

-- Staging: one parsed workbook line (project label, date, hours, description) plus the
-- importer's review edits. Deleted with the import (discard); kept after commit as the
-- record of what was reviewed.
CREATE TABLE timesheet_import_rows (
    id              uuid PRIMARY KEY,
    import_id       uuid NOT NULL REFERENCES timesheet_imports (id) ON DELETE CASCADE,

    -- Provenance: which sheet/row of the workbook this came from.
    sheet_name      text NOT NULL,
    sheet_row       int NOT NULL,

    -- The workbook's project label, as parsed (trimmed). Matching to a project is by
    -- code first, then saved mappings; unmatched rows are mapped by hand on review.
    project_label   text NOT NULL,

    entry_date      date NOT NULL,
    hours           numeric(5,2) NOT NULL CHECK (hours > 0),
    description     text,

    -- Review state: resolved project, billing role, and whether the row commits.
    project_id      uuid REFERENCES projects (id),
    billing_role_id uuid REFERENCES roles (id),
    included        boolean NOT NULL DEFAULT true,

    updated_at      timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_timesheet_import_rows_import ON timesheet_import_rows (import_id, entry_date);

-- Remembers "workbook label → project" so later imports auto-map labels that don't
-- match a project code (design-doc.md §5.2 timesheet_import_mappings).
CREATE TABLE timesheet_import_mappings (
    id         uuid PRIMARY KEY,
    label      text NOT NULL,
    project_id uuid NOT NULL REFERENCES projects (id),
    created_by uuid REFERENCES employees (id),
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_timesheet_import_mappings_label ON timesheet_import_mappings (lower(label));

-- Entries created by an import carry its id (0006 reserved the column for this script).
ALTER TABLE time_entries ADD COLUMN import_id uuid REFERENCES timesheet_imports (id);
CREATE INDEX ix_time_entries_import ON time_entries (import_id) WHERE import_id IS NOT NULL;
