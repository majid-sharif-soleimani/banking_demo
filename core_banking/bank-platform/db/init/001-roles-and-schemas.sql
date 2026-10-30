-- One login role and one schema per service in the database "bank" (design plan ADR 11, bank-platform ADR 0001).
--
-- PostgreSQL runs this file once, when the container starts with an empty data directory, through psql connected
-- to POSTGRES_DB as the admin user. Both the Aspire AppHost and docker-compose.yml set POSTGRES_DB=bank.
--
-- Passwords are never written here. Each one is read from an environment variable of the container
-- (<SERVICE>_DB_PASSWORD), which the AppHost fills from a secret parameter and Docker Compose from .env.
-- A missing variable stops the script with a message naming it, and the container does not start.
--
-- Each role:
--   - can connect to this database and owns its schema, so it can create and use tables there;
--   - has its schema as search path, so EF Core models name no schema;
--   - has no USAGE on any other schema, so it cannot see another service's tables;
--   - cannot create tables in "public" or new schemas.

\set ON_ERROR_STOP on

-- 1. Read every password first, so a missing one stops the script before anything is created.
\getenv operations_password OPERATIONS_DB_PASSWORD
\if :{?operations_password}
\else
    DO $$ BEGIN RAISE EXCEPTION 'OPERATIONS_DB_PASSWORD is not set. Set it in the AppHost user secrets or in .env.'; END $$;
\endif

\getenv customers_password CUSTOMERS_DB_PASSWORD
\if :{?customers_password}
\else
    DO $$ BEGIN RAISE EXCEPTION 'CUSTOMERS_DB_PASSWORD is not set. Set it in the AppHost user secrets or in .env.'; END $$;
\endif

\getenv accounts_password ACCOUNTS_DB_PASSWORD
\if :{?accounts_password}
\else
    DO $$ BEGIN RAISE EXCEPTION 'ACCOUNTS_DB_PASSWORD is not set. Set it in the AppHost user secrets or in .env.'; END $$;
\endif

\getenv cards_password CARDS_DB_PASSWORD
\if :{?cards_password}
\else
    DO $$ BEGIN RAISE EXCEPTION 'CARDS_DB_PASSWORD is not set. Set it in the AppHost user secrets or in .env.'; END $$;
\endif

\getenv lending_password LENDING_DB_PASSWORD
\if :{?lending_password}
\else
    DO $$ BEGIN RAISE EXCEPTION 'LENDING_DB_PASSWORD is not set. Set it in the AppHost user secrets or in .env.'; END $$;
\endif

\getenv ledger_password LEDGER_DB_PASSWORD
\if :{?ledger_password}
\else
    DO $$ BEGIN RAISE EXCEPTION 'LEDGER_DB_PASSWORD is not set. Set it in the AppHost user secrets or in .env.'; END $$;
\endif

-- 2. Nobody but the admin may use the database or the public schema unless granted below.
REVOKE ALL ON DATABASE :"DBNAME" FROM PUBLIC;
REVOKE ALL ON SCHEMA public FROM PUBLIC;

-- 3. One role and one schema per service.

-- operations -------------------------------------------------------------------------------------------------------
CREATE ROLE operations LOGIN PASSWORD :'operations_password';
GRANT CONNECT ON DATABASE :"DBNAME" TO operations;
CREATE SCHEMA operations AUTHORIZATION operations;
ALTER ROLE operations SET search_path = operations;

-- customers --------------------------------------------------------------------------------------------------------
CREATE ROLE customers LOGIN PASSWORD :'customers_password';
GRANT CONNECT ON DATABASE :"DBNAME" TO customers;
CREATE SCHEMA customers AUTHORIZATION customers;
ALTER ROLE customers SET search_path = customers;

-- accounts ---------------------------------------------------------------------------------------------------------
CREATE ROLE accounts LOGIN PASSWORD :'accounts_password';
GRANT CONNECT ON DATABASE :"DBNAME" TO accounts;
CREATE SCHEMA accounts AUTHORIZATION accounts;
ALTER ROLE accounts SET search_path = accounts;

-- cards ------------------------------------------------------------------------------------------------------------
CREATE ROLE cards LOGIN PASSWORD :'cards_password';
GRANT CONNECT ON DATABASE :"DBNAME" TO cards;
CREATE SCHEMA cards AUTHORIZATION cards;
ALTER ROLE cards SET search_path = cards;

-- lending ----------------------------------------------------------------------------------------------------------
CREATE ROLE lending LOGIN PASSWORD :'lending_password';
GRANT CONNECT ON DATABASE :"DBNAME" TO lending;
CREATE SCHEMA lending AUTHORIZATION lending;
ALTER ROLE lending SET search_path = lending;

-- ledger -----------------------------------------------------------------------------------------------------------
CREATE ROLE ledger LOGIN PASSWORD :'ledger_password';
GRANT CONNECT ON DATABASE :"DBNAME" TO ledger;
CREATE SCHEMA ledger AUTHORIZATION ledger;
ALTER ROLE ledger SET search_path = ledger;
