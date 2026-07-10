-- Project codes are unique per client, not globally: the same short code may recur
-- across different clients, but never twice within one client. Replaces the original
-- global UNIQUE on projects.code (constraint projects_code_key from 0001).
ALTER TABLE projects DROP CONSTRAINT projects_code_key;
ALTER TABLE projects ADD CONSTRAINT ux_projects_client_code UNIQUE (client_id, code);
