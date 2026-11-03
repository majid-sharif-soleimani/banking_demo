import { useState, type FormEvent } from "react";
import { postAction, uploadDocument } from "./api";

// The components the person answers (phase 2 stage 3). Each one sends its answer to the agent host, not to the model:
// the host checks it, acts, and stores the outcome. Then `onDone` sends a short chat message so the agent continues.
// In an old chat (`readOnly`) they show what was asked, without buttons.

type Done = (message: string) => void;

export type FormArgs = {
  formId: string;
  title: string;
  submitLabel: string;
  fields: { name: string; label: string; type: "text" | "number" | "date" | "choice"; required: boolean; options?: string[] | null; min?: number | null; max?: number | null }[];
};

export function FormView({ args, onDone, readOnly }: { args?: Partial<FormArgs>; onDone?: Done; readOnly?: boolean }) {
  const fields = args?.fields ?? [];
  const [values, setValues] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [state, setState] = useState<"open" | "busy" | "sent" | "error">("open");

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!args?.formId) {
      return;
    }

    setState("busy");
    const answer = await postAction(`/agent/forms/${encodeURIComponent(args.formId)}`, { values });
    if (answer.ok) {
      setState("sent");
      onDone?.("I sent the form.");
    } else {
      setErrors(answer.errors ?? {});
      setState(answer.code === "invalid_values" ? "open" : "error");
    }
  }

  const locked = readOnly || state === "busy" || state === "sent";
  return (
    <form className="component form-card" onSubmit={submit} aria-label={args?.title}>
      {args?.title && <h2>{args.title}</h2>}
      {fields.map((field) => (
        <label key={field.name}>
          <span>
            {field.label}
            {field.required && <span aria-hidden="true"> *</span>}
          </span>
          {field.type === "choice" ? (
            <select
              name={field.name}
              required={field.required}
              disabled={locked}
              value={values[field.name] ?? ""}
              onChange={(e) => setValues({ ...values, [field.name]: e.target.value })}
            >
              <option value="" />
              {(field.options ?? []).map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </select>
          ) : (
            <input
              name={field.name}
              type={field.type === "number" ? "number" : field.type === "date" ? "date" : "text"}
              step={field.type === "number" ? "any" : undefined}
              min={field.min ?? undefined}
              max={field.max ?? undefined}
              required={field.required}
              disabled={locked}
              value={values[field.name] ?? ""}
              onChange={(e) => setValues({ ...values, [field.name]: e.target.value })}
              dir="auto"
            />
          )}
          {errors[field.name] && (
            <span className="error" role="alert">
              {errors[field.name]}
            </span>
          )}
        </label>
      ))}
      {!readOnly && (
        <button type="submit" disabled={locked}>
          {state === "sent" ? "Sent" : (args?.submitLabel ?? "Send")}
        </button>
      )}
      {state === "error" && <p className="error">This form can no longer be sent. Ask again in the chat.</p>}
    </form>
  );
}

export type ConfirmationArgs = { changeId: string; title: string; fields: { label: string; value: string }[] };

export function ConfirmationView({ args, onDone, readOnly }: { args?: Partial<ConfirmationArgs>; onDone?: Done; readOnly?: boolean }) {
  const [state, setState] = useState<"open" | "busy" | "done" | "failed" | "cancelled" | "gone">("open");
  const [message, setMessage] = useState<string>();

  async function answer(action: "confirm" | "cancel") {
    if (!args?.changeId) {
      return;
    }

    setState("busy");
    const result = await postAction(`/agent/changes/${encodeURIComponent(args.changeId)}/${action}`);
    if (!result.ok) {
      setState("gone");
      return;
    }

    setState(result.status);
    setMessage(result.message);
    onDone?.(result.status === "cancelled" ? "I cancelled the change." : "I confirmed the change.");
  }

  return (
    <section className="component card confirmation" aria-label={args?.title}>
      <h2>{args?.title ?? "Confirm"}</h2>
      <dl>
        {(args?.fields ?? []).map((field, i) => (
          <div key={i} className="field">
            <dt>{field.label}</dt>
            <dd dir="auto">{field.value}</dd>
          </div>
        ))}
      </dl>
      {!readOnly && state === "open" && (
        <div className="actions">
          <button type="button" onClick={() => void answer("confirm")}>
            Confirm
          </button>
          <button type="button" className="secondary" onClick={() => void answer("cancel")}>
            Cancel
          </button>
        </div>
      )}
      {state === "busy" && <p className="notice">Sending…</p>}
      {state === "done" && <p className="notice" role="status">Done.</p>}
      {state === "cancelled" && <p className="notice" role="status">Cancelled. Nothing was changed.</p>}
      {state === "failed" && (
        <p className="error" role="alert">
          The bank did not make the change{message ? `: ${message}` : "."}
        </p>
      )}
      {state === "gone" && <p className="error">This change can no longer be confirmed. Ask again in the chat.</p>}
    </section>
  );
}

export type DocumentRequestArgs = { requestId: string; applicationId: string; description: string };

export function DocumentRequestView({ args, onDone, readOnly }: { args?: Partial<DocumentRequestArgs>; onDone?: Done; readOnly?: boolean }) {
  const [file, setFile] = useState<File>();
  const [state, setState] = useState<"open" | "busy" | "done" | "failed">("open");

  async function send(event: FormEvent) {
    event.preventDefault();
    if (!args?.requestId || !file) {
      return;
    }

    setState("busy");
    const answer = await uploadDocument(args.requestId, file);
    setState(answer.ok && answer.status === "done" ? "done" : "failed");
    if (answer.ok) {
      onDone?.(`I uploaded ${file.name}.`);
    }
  }

  return (
    <form className="component form-card" onSubmit={send} aria-label="Upload a document">
      <h2>Upload a document</h2>
      <p dir="auto">{args?.description}</p>
      {!readOnly && (
        <>
          <input type="file" accept="application/pdf,image/png,image/jpeg" disabled={state !== "open"} onChange={(e) => setFile(e.target.files?.[0])} />
          <button type="submit" disabled={!file || state !== "open"}>
            Upload
          </button>
        </>
      )}
      {state === "done" && <p className="notice" role="status">Uploaded.</p>}
      {state === "failed" && <p className="error">The upload failed. Use one PDF, PNG or JPEG of at most 5 MB, and ask again in the chat.</p>}
    </form>
  );
}
