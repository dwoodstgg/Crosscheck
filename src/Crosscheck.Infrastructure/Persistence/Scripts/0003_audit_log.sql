-- Append-only record of sensitive mutations (design-doc.md §5.2): role grants,
-- rate/budget changes, invoice issuance, close-outs, imports, Admin overrides.
-- Never updated or deleted by the application.

CREATE TABLE audit_log (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    occurred_at       timestamptz NOT NULL DEFAULT now(),
    actor_employee_id uuid REFERENCES employees (id),
    action            text NOT NULL,      -- e.g. 'role.granted', 'employee.deactivated'
    entity_type       text NOT NULL,      -- e.g. 'employee', 'project', 'invoice'
    entity_id         uuid,
    details           jsonb
);

CREATE INDEX ix_audit_log_entity ON audit_log (entity_type, entity_id);
CREATE INDEX ix_audit_log_occurred_at ON audit_log (occurred_at);
