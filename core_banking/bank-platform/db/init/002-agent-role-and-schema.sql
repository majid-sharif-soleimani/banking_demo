-- The agent host's role and schema "agent" (phase 2 design plan, decision A10): chat history, same rules as a bank
-- service (bank-platform ADR 0001). It runs after 001, once, on an empty data directory.
--
-- The agent host is a client of the bank, not a bank service, and not every deployment runs it yet. So this file is
-- optional: without AGENT_DB_PASSWORD it creates nothing and says so. The AppHost always sets it; Docker Compose sets it
-- once the agent runs there (phase 2 roadmap, step 6). On a database created before this file existed, run these
-- statements by hand as the admin user.

\set ON_ERROR_STOP on

\getenv agent_password AGENT_DB_PASSWORD
\if :{?agent_password}
CREATE ROLE agent LOGIN PASSWORD :'agent_password';
GRANT CONNECT ON DATABASE :"DBNAME" TO agent;
CREATE SCHEMA agent AUTHORIZATION agent;
ALTER ROLE agent SET search_path = agent;
\else
\echo 'AGENT_DB_PASSWORD is not set: no agent role or schema created.'
\endif
