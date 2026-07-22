-- Project modules (design-doc §5.2, decision #21): a work order's budget broken into named
-- sub-sections ("Ag Chem", "Water Levels Design", "Supplemental Hours" — or numbered
-- milestones), each with its own hour budget (flat or per-role), optional per-role rate
-- overrides, and an optional agreed fixed billing amount. Some clients call the breakdown
-- "modules", others "milestones" — mechanics are identical, so the label is a per-project
-- display choice.

ALTER TABLE projects ADD COLUMN breakdown_label text NOT NULL DEFAULT 'module'
    CHECK (breakdown_label IN ('module', 'milestone'));

CREATE TABLE project_modules (
    id         uuid PRIMARY KEY,
    project_id uuid NOT NULL REFERENCES projects (id),
    name       text NOT NULL,
    sort_order int  NOT NULL DEFAULT 0,

    -- Explicit flat hour budget (milestone style: "Milestone 5 — 120h"). Null = the module's
    -- effective hours derive from the sum of its per-role allocations below.
    hours      numeric(9,2) CHECK (hours >= 0),

    -- Agreed fixed billing amount. Set = fixed-price: the client is billed exactly this,
    -- hours are internal budgeting only. Null = T&M: bills hours × resolved rate as incurred.
    amount     numeric(12,2) CHECK (amount >= 0),

    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    deleted_at timestamptz          -- soft delete only (design rule 11)
);

-- One live module name per project (case-insensitive).
CREATE UNIQUE INDEX uq_project_modules_live_name
    ON project_modules (project_id, lower(name)) WHERE deleted_at IS NULL;

CREATE INDEX ix_project_modules_project ON project_modules (project_id);

-- Composite target so time_entries (0016) can enforce module-belongs-to-project in the DB.
ALTER TABLE project_modules ADD CONSTRAINT uq_project_modules_id_project UNIQUE (id, project_id);

-- Per-role hour allocations per module (mirrors budget_role_allocations; the set is replaced
-- wholesale on save, so plain CASCADE rows — the module itself never hard-deletes).
CREATE TABLE project_module_allocations (
    id        uuid PRIMARY KEY,
    module_id uuid NOT NULL REFERENCES project_modules (id) ON DELETE CASCADE,
    role_id   uuid NOT NULL REFERENCES roles (id),
    hours     numeric(9,2) NOT NULL CHECK (hours >= 0),

    UNIQUE (module_id, role_id)
);

CREATE INDEX ix_project_module_allocations_module ON project_module_allocations (module_id);

-- Per-role rate overrides per module. role_id NULL = module-wide rate (any role, e.g.
-- "Ongoing Maintenance billed at $85/hr"). Resolution for an entry: (module, entry's role)
-- override → (module, NULL) override → (project, entry's role) rate card. Same live-row +
-- soft-delete discipline as project_rate_cards (0013); NULLS NOT DISTINCT keeps the
-- module-wide row unique too.
CREATE TABLE project_module_rates (
    id          uuid PRIMARY KEY,
    module_id   uuid NOT NULL REFERENCES project_modules (id),
    role_id     uuid REFERENCES roles (id),
    hourly_rate numeric(10,2) NOT NULL CHECK (hourly_rate >= 0),
    created_at  timestamptz NOT NULL DEFAULT now(),
    deleted_at  timestamptz
);

CREATE UNIQUE INDEX uq_project_module_rates_live
    ON project_module_rates (module_id, role_id) NULLS NOT DISTINCT
    WHERE deleted_at IS NULL;
