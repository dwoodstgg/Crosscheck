-- Billing contact & terms move to the project, with the client as a fallback default
-- (design-doc.md §5.2, decision 18). A client serves several projects/departments with
-- different contacts, so each project may carry its own contact/address/terms; a null
-- project field inherits the client's. Effective value resolves project → client → default.

-- Client fields become pure defaults: payment_terms_days may now be null (billing contact
-- columns were already nullable). Existing rows keep their values.
ALTER TABLE clients ALTER COLUMN payment_terms_days DROP NOT NULL;
ALTER TABLE clients ALTER COLUMN payment_terms_days DROP DEFAULT;

-- Project-level overrides, all nullable (null = inherit the client's default).
ALTER TABLE projects ADD COLUMN billing_contact_name  text;
ALTER TABLE projects ADD COLUMN billing_contact_email text;
ALTER TABLE projects ADD COLUMN billing_address       jsonb;
ALTER TABLE projects ADD COLUMN payment_terms_days    int CHECK (payment_terms_days IS NULL OR payment_terms_days >= 0);
