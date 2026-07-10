-- Rates are now fixed for the life of a project — one live row per (project, billing role),
-- set from the contract, never effective-dated. Assignments no longer carry an active-date
-- window: an employee stays active for the life of the project (end_date is kept purely as a
-- soft-deactivate marker). Invoice-line rate snapshots are unaffected (they live on invoices).

-- Rate cards ------------------------------------------------------------------

-- Collapse any existing effective-dated history to a single live row per (project, role):
-- keep the most recent live row (max effective_from, id as tiebreak), soft-delete the rest.
UPDATE project_rate_cards rc
SET deleted_at = now()
WHERE rc.deleted_at IS NULL
  AND EXISTS (
      SELECT 1 FROM project_rate_cards other
      WHERE other.project_id = rc.project_id
        AND other.role_id = rc.role_id
        AND other.deleted_at IS NULL
        AND (other.effective_from > rc.effective_from
             OR (other.effective_from = rc.effective_from AND other.id > rc.id))
  );

ALTER TABLE project_rate_cards DROP CONSTRAINT ex_rate_cards_no_overlap;
DROP INDEX ix_rate_cards_project_role;

ALTER TABLE project_rate_cards DROP COLUMN effective_from;
ALTER TABLE project_rate_cards DROP COLUMN effective_to;

-- At most one live rate per (project, role).
CREATE UNIQUE INDEX ux_rate_cards_project_role_live
    ON project_rate_cards (project_id, role_id) WHERE deleted_at IS NULL;

-- Assignments -----------------------------------------------------------------

-- Dropping start_date also drops the inline (end_date >= start_date) CHECK that references it.
ALTER TABLE project_assignments DROP COLUMN start_date;
