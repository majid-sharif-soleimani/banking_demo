-- Outbox and inbox of one service, created in that service's schema (the connection's search_path).
-- version is PostgreSQL's xmin system column, as on every table; it needs no column here.

CREATE TABLE outbox_message (
    id           uuid PRIMARY KEY,                           -- the event's EventId, sent as the message-id header
    position     bigint GENERATED ALWAYS AS IDENTITY,        -- commit-independent send order
    schema_uri   text        NOT NULL,
    message_key  text        NOT NULL,
    payload      jsonb       NOT NULL,
    occurred_on  date        NOT NULL,
    performed_by text        NOT NULL,
    created_at   timestamptz NOT NULL,
    sent_at      timestamptz NULL,
    attempts     integer     NOT NULL DEFAULT 0,             -- failed sends, with the last error, for diagnosis
    last_error   text        NULL
);

-- The dispatcher only ever reads unsent rows, in order.
CREATE INDEX ix_outbox_message_unsent ON outbox_message (position) WHERE sent_at IS NULL;

CREATE TABLE inbox_message (
    message_id   uuid        NOT NULL,
    handler_name text        NOT NULL,
    schema_uri   text        NOT NULL,
    performed_by text        NOT NULL,
    created_at   timestamptz NOT NULL,
    -- ADR 0007: one row per handler, so two handlers in one service can each process the same message once.
    PRIMARY KEY (message_id, handler_name)
);
