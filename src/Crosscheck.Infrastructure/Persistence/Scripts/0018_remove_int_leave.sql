-- Retire the seeded INT-LEAVE project. Leave is no longer logged as time at all: the
-- timesheet auto-credits 8h of Holiday leave for each untouched company holiday
-- (company_holidays, 0017) and derives Personal leave as expected-monthly-hours minus
-- hours entered. A dedicated leave project would only confuse people.
--
-- Where the project was never used it is removed outright, along with any seeded/derived
-- children. If time was ever logged against it, the rows must survive (nothing hard-deletes
-- once used) — the project is archived instead, which hides it from timesheets.
DO $$
DECLARE leave_project uuid := 'd0000000-0000-0000-0000-000000000001';
BEGIN
    IF EXISTS (SELECT 1 FROM time_entries WHERE project_id = leave_project) THEN
        UPDATE projects SET status = 'archived' WHERE id = leave_project;
        UPDATE project_assignments SET end_date = CURRENT_DATE
        WHERE project_id = leave_project AND end_date IS NULL;
    ELSE
        DELETE FROM project_module_rates
        WHERE module_id IN (SELECT id FROM project_modules WHERE project_id = leave_project);
        DELETE FROM project_module_allocations
        WHERE module_id IN (SELECT id FROM project_modules WHERE project_id = leave_project);
        DELETE FROM project_modules WHERE project_id = leave_project;
        DELETE FROM project_rate_cards WHERE project_id = leave_project;
        DELETE FROM budget_alerts
        WHERE budget_id IN (SELECT id FROM budgets WHERE project_id = leave_project);
        DELETE FROM budget_role_allocations
        WHERE budget_id IN (SELECT id FROM budgets WHERE project_id = leave_project);
        DELETE FROM budget_revisions
        WHERE budget_id IN (SELECT id FROM budgets WHERE project_id = leave_project);
        DELETE FROM budgets WHERE project_id = leave_project;
        DELETE FROM project_assignments WHERE project_id = leave_project;
        DELETE FROM projects WHERE id = leave_project;
    END IF;
END $$;
