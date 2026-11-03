import { useEffect, useState } from "react";
import { getChat, listChats, type ChatSummary, type StoredChat, type StoredMessage } from "./api";
import { Chart } from "./chart";
import { Card, Table } from "./components";
import { Diagram } from "./diagram";
import { ConfirmationView, DocumentRequestView, FormView } from "./interactive";

// The old chats (phase 2 design plan, requirement 2.1.6): a customer sees their own, the admin every chat. Old chats
// are read-only: the same components as in a live chat, without buttons, plus a short line for each thing the person
// did (a form sent, a change confirmed, a document uploaded).

type Props = { isAdmin: boolean; onBack: () => void };

export function History({ isAdmin, onBack }: Props) {
  const [chats, setChats] = useState<ChatSummary[]>();
  const [open, setOpen] = useState<StoredChat>();

  useEffect(() => {
    void listChats().then(setChats);
  }, []);

  if (open) {
    return (
      <section className="history" aria-label="Old chat">
        <button type="button" className="secondary" onClick={() => setOpen(undefined)}>
          Back to the list
        </button>
        <p className="notice">
          {isAdmin ? `${open.username} · ` : ""}
          {new Date(open.openedAt).toLocaleString()} · read-only
        </p>
        <div className="messages">
          {open.messages.map((m, i) => (
            <div key={i} className={`bubble ${m.author}`} dir="auto">
              <Stored message={m} />
            </div>
          ))}
        </div>
      </section>
    );
  }

  return (
    <section className="history" aria-label="Old chats">
      <button type="button" className="secondary" onClick={onBack}>
        Back to the chat
      </button>
      {chats === undefined && <p className="loading">Loading…</p>}
      {chats?.length === 0 && <p className="notice">No chats yet.</p>}
      <ul className="chat-list">
        {chats?.map((c) => (
          <li key={c.id}>
            <button type="button" className="link" onClick={() => void getChat(c.id).then(setOpen)}>
              <span dir="auto">{c.firstQuestion ?? "(no question)"}</span>
              <span className="notice">
                {isAdmin ? `${c.username} · ` : ""}
                {new Date(c.openedAt).toLocaleString()} · {c.status === "Open" ? "open" : "closed"}
              </span>
            </button>
          </li>
        ))}
      </ul>
    </section>
  );
}

function parse(content: string): Record<string, unknown> {
  try {
    return JSON.parse(content) as Record<string, unknown>;
  } catch {
    return {};
  }
}

/** One stored message, drawn by its kind (agent host MessageKind). */
export function Stored({ message }: { message: StoredMessage }) {
  const data = parse(message.content);
  switch (message.kind) {
    case "text":
      return <p>{message.content}</p>;
    case "table":
      return <Table args={data} />;
    case "card":
      return <Card args={data} />;
    case "chart":
      return <Chart args={data} />;
    case "diagram":
      return <Diagram args={data} />;
    case "form":
      return <FormView args={data} readOnly />;
    case "confirmation":
      return <ConfirmationView args={data} readOnly />;
    case "document_request":
      return <DocumentRequestView args={data} readOnly />;
    case "form_values":
      return <p>Sent the form “{String(data.title ?? "")}”.</p>;
    case "change_result":
      return <p>{data.status === "cancelled" ? "Cancelled" : data.status === "done" ? "Confirmed" : "Confirmed, but the bank refused"}: {String(data.title ?? "")}.</p>;
    case "document":
      return <p>Uploaded {String(data.fileName ?? "a document")} ({String(data.status ?? "")}).</p>;
    default:
      return <p>{message.content}</p>;
  }
}
