import { useState, type FormEvent } from "react";
import { postAction, uploadDocument } from "./api";
import { useText } from "./i18n";

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
  const t = useText();

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!args?.formId) {
      return;
    }

    setState("busy");
    const answer = await postAction(`/agent/forms/${encodeURIComponent(args.formId)}`, { values });
    if (answer.ok) {
      setState("sent");
      onDone?.(t("iSentTheForm"));
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
          {state === "sent" ? t("sent") : (args?.submitLabel ?? t("send"))}
        </button>
      )}
      {state === "error" && <p className="error">{t("formGone")}</p>}
    </form>
  );
}

export type ConfirmationArgs = { changeId: string; title: string; fields: { label: string; value: string }[] };

export function ConfirmationView({ args, onDone, readOnly }: { args?: Partial<ConfirmationArgs>; onDone?: Done; readOnly?: boolean }) {
  const [state, setState] = useState<"open" | "busy" | "done" | "failed" | "cancelled" | "gone" | "unknown">("open");
  const [message, setMessage] = useState<string>();
  const t = useText();

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
    // No answer from the bank: the buttons stay, and the agent is told nothing until the person tries again.
    if (result.status !== "unknown") {
      onDone?.(result.status === "cancelled" ? t("iCancelled") : t("iConfirmed"));
    }
  }

  return (
    <section className="component card confirmation" aria-label={args?.title}>
      <h2>{args?.title ?? t("confirm")}</h2>
      <dl>
        {(args?.fields ?? []).map((field, i) => (
          <div key={i} className="field">
            <dt>{field.label}</dt>
            <dd dir="auto">{field.value}</dd>
          </div>
        ))}
      </dl>
      {!readOnly && (state === "open" || state === "unknown") && (
        <div className="actions">
          <button type="button" onClick={() => void answer("confirm")}>
            {t("confirm")}
          </button>
          <button type="button" className="secondary" onClick={() => void answer("cancel")}>
            {t("cancel")}
          </button>
        </div>
      )}
      {state === "busy" && <p className="notice">{t("sending")}</p>}
      {state === "done" && <p className="notice" role="status">{t("done")}</p>}
      {state === "cancelled" && <p className="notice" role="status">{t("cancelledNothingChanged")}</p>}
      {state === "failed" && (
        <p className="error" role="alert">
          {t("bankRefused")}
          {message ? `: ${message}` : "."}
        </p>
      )}
      {state === "gone" && <p className="error">{t("changeGone")}</p>}
      {state === "unknown" && (
        <p className="error" role="alert">
          {t("bankNoAnswer")}
        </p>
      )}
    </section>
  );
}

export type DocumentRequestArgs = { requestId: string; applicationId: string; description: string };

export function DocumentRequestView({ args, onDone, readOnly }: { args?: Partial<DocumentRequestArgs>; onDone?: Done; readOnly?: boolean }) {
  const [file, setFile] = useState<File>();
  const [state, setState] = useState<"open" | "busy" | "done" | "failed" | "unknown">("open");
  const t = useText();

  async function send(event: FormEvent) {
    event.preventDefault();
    if (!args?.requestId || !file) {
      return;
    }

    setState("busy");
    const answer = await uploadDocument(args.requestId, file);
    const next = !answer.ok ? "failed" : answer.status === "done" ? "done" : answer.status === "unknown" ? "unknown" : "failed";
    setState(next);
    if (answer.ok && next !== "unknown") {
      onDone?.(t("iUploaded", { file: file.name }));
    }
  }

  return (
    <form className="component form-card" onSubmit={send} aria-label={t("uploadDocument")}>
      <h2>{t("uploadDocument")}</h2>
      <p dir="auto">{args?.description}</p>
      {!readOnly && (
        <>
          <input type="file" accept="application/pdf,image/png,image/jpeg" disabled={state !== "open" && state !== "unknown"} onChange={(e) => setFile(e.target.files?.[0])} />
          <button type="submit" disabled={!file || (state !== "open" && state !== "unknown")}>
            {t("upload")}
          </button>
        </>
      )}
      {state === "done" && <p className="notice" role="status">{t("uploaded")}</p>}
      {state === "failed" && <p className="error">{t("uploadFailed")}</p>}
      {state === "unknown" && <p className="error">{t("uploadNoAnswer")}</p>}
    </form>
  );
}
