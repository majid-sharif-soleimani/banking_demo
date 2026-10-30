-- The sender's own state change, written in the same transaction as the event.
CREATE TABLE test_item (
    id           uuid PRIMARY KEY,
    name         text        NOT NULL,
    performed_by text        NOT NULL,
    created_at   timestamptz NOT NULL
);

-- The receiver's effect. (message_key, sequence) is the business key: the same fact under a new message id is rejected.
CREATE TABLE received_test_event (
    event_id     uuid        NOT NULL,
    message_key  text        NOT NULL,
    sequence     integer     NOT NULL,
    amount       numeric     NOT NULL,
    currency     text        NOT NULL,
    performed_by text        NOT NULL,
    created_at   timestamptz NOT NULL,
    CONSTRAINT uq_received_test_event_business_key UNIQUE (message_key, sequence)
);

-- Written by a second handler of the same event.
CREATE TABLE audited_test_event (
    event_id     uuid PRIMARY KEY,
    performed_by text        NOT NULL,
    created_at   timestamptz NOT NULL
);
