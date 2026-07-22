-- Company holiday calendar (design-doc §5: company_holidays). Admin-managed; drives the
-- greyed-out holiday columns on timesheets, the workday count behind the expected monthly
-- hours, and (Phase 2) reconciling imported "Leave - Holiday" rows. Time can still be
-- logged on a holiday — the calendar is informational, never a hard block.

CREATE TABLE company_holidays (
    id           uuid PRIMARY KEY,
    holiday_date date NOT NULL,
    name         text NOT NULL,
    created_by   uuid REFERENCES employees (id),
    created_at   timestamptz NOT NULL DEFAULT now(),
    deleted_at   timestamptz,        -- soft delete only (design rule 11)
    deleted_by   uuid REFERENCES employees (id)
);

-- One live holiday per date; a deleted holiday's date can be re-added.
CREATE UNIQUE INDEX uq_company_holidays_live_date
    ON company_holidays (holiday_date) WHERE deleted_at IS NULL;
