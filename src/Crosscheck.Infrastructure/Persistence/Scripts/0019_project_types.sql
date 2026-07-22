-- Project types (hourly / fixed_rate / service_contract) replace the budget-level "type"
-- picker: the contract shape lives on the project, and the budget form adapts to it.
-- The old time-and-materials wording is retired — 'time_and_materials_cap' and 'hours_cap'
-- budgets are hourly projects' caps, 'fixed_fee' budgets belong to fixed-rate projects.
-- Service contracts run for the project's start–end timeframe and bill monthly; a fixed
-- monthly amount is stored in budgets.monthly_amount with amount holding the contract
-- total it sizes (monthly × months), so burn and alerts keep measuring the whole
-- engagement and heavy/light months average out.

ALTER TABLE projects ADD COLUMN project_type text NOT NULL DEFAULT 'hourly'
    CHECK (project_type IN ('hourly', 'fixed_rate', 'service_contract'));

-- Projects that already carry a fixed-fee budget are fixed-rate projects.
UPDATE projects p
SET project_type = 'fixed_rate'
FROM budgets b
WHERE b.project_id = p.id AND b.type = 'fixed_fee';

-- budgets.type now mirrors the project type at save time.
ALTER TABLE budgets DROP CONSTRAINT budgets_type_check;
UPDATE budgets SET type = CASE WHEN type = 'fixed_fee' THEN 'fixed_rate' ELSE 'hourly' END;
ALTER TABLE budgets ADD CONSTRAINT budgets_type_check
    CHECK (type IN ('hourly', 'fixed_rate', 'service_contract'));

ALTER TABLE budgets ADD COLUMN monthly_amount numeric(12,2)
    CHECK (monthly_amount IS NULL OR monthly_amount >= 0);

ALTER TABLE budget_revisions DROP CONSTRAINT budget_revisions_from_type_check;
ALTER TABLE budget_revisions DROP CONSTRAINT budget_revisions_to_type_check;
UPDATE budget_revisions SET
    from_type = CASE
        WHEN from_type IS NULL THEN NULL
        WHEN from_type = 'fixed_fee' THEN 'fixed_rate'
        ELSE 'hourly'
    END,
    to_type = CASE WHEN to_type = 'fixed_fee' THEN 'fixed_rate' ELSE 'hourly' END;
ALTER TABLE budget_revisions ADD CONSTRAINT budget_revisions_from_type_check
    CHECK (from_type IS NULL OR from_type IN ('hourly', 'fixed_rate', 'service_contract'));
ALTER TABLE budget_revisions ADD CONSTRAINT budget_revisions_to_type_check
    CHECK (to_type IN ('hourly', 'fixed_rate', 'service_contract'));
