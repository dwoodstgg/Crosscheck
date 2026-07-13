-- Time entry + approval (design-doc.md §5.2, §6.1). No submission step: entries are
-- born 'open' and owner-editable until the covering semi-monthly window is closed or
-- the entry is approved. Money/rate columns arrive with invoicing (Phase 2); task_id,
-- milestone_id, invoice_line_id, and import_id are added by the later scripts that
-- create the tables they reference.

-- Semi-monthly edit windows (1st–15th, 16th–EOM). A window is open unless a row here
-- marks it closed; closing locks owner edits for entries in [period_start, period_end].
-- One row per window (period_start is unique). Close/reopen are audited in audit_log.
CREATE TABLE timesheet_periods (
    id           uuid PRIMARY KEY,
    period_start date NOT NULL UNIQUE,
    period_end   date NOT NULL,
    status       text NOT NULL DEFAULT 'open' CHECK (status IN ('open', 'closed')),
    closed_by    uuid REFERENCES employees (id),
    closed_at    timestamptz,
    CONSTRAINT ck_timesheet_periods_range CHECK (period_end >= period_start)
);

CREATE TABLE time_entries (
    id              uuid PRIMARY KEY,
    project_id      uuid NOT NULL REFERENCES projects (id),
    employee_id     uuid NOT NULL REFERENCES employees (id),

    -- Billing role is chosen per entry (design rule 3) — the kind of work can change
    -- day to day. Rate resolution is (project, this role, entry_date). Company roles
    -- (permissions) are separate.
    billing_role_id uuid NOT NULL REFERENCES roles (id),

    entry_date      date NOT NULL,

    -- hours_worked is only ever set by the owner; hours_billed defaults to worked and
    -- is adjusted only by an approver at approval (design rules 6). numeric, never float.
    hours_worked    numeric(5,2) NOT NULL CHECK (hours_worked >= 0),
    hours_billed    numeric(5,2) NOT NULL CHECK (hours_billed >= 0),

    notes           text,

    -- Seeded from the project/client (internal → non-billable) at create; authoritative
    -- on the entry. Task-driven default arrives with the tasks table (Phase 2).
    is_billable     boolean NOT NULL DEFAULT true,

    -- open → approved → invoiced. No submission step. 'invoiced' is set by invoicing
    -- (Phase 2) and is never editable — void the invoice instead.
    status          text NOT NULL DEFAULT 'open'
                    CHECK (status IN ('open', 'approved', 'invoiced')),

    -- Approval is the billing decision (sets hours_billed); recorded here.
    approved_by     uuid REFERENCES employees (id),
    approved_at     timestamptz,

    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),

    -- One entry per person per project per day (matches the import dedupe rule and the
    -- monthly grid, where a cell is a single project/day entry).
    CONSTRAINT uq_time_entries_person_project_date UNIQUE (employee_id, project_id, entry_date)
);

-- Grid: an employee's month. Approvals: a project's open/approved entries in a window.
CREATE INDEX ix_time_entries_employee_date ON time_entries (employee_id, entry_date);
CREATE INDEX ix_time_entries_project_status_date ON time_entries (project_id, status, entry_date);
