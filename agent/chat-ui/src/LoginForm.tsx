import { useState, type FormEvent } from "react";
import { Guide } from "./Guide";
import { useText } from "./i18n";

type Props = {
  onSubmit: (username: string, password: string) => Promise<string | undefined>;
  notice?: string;
};

// The login form, drawn by the UI itself: its values go only to POST /auth/login, never into a chat message or to the
// model (phase 2 design plan, flow 4.1). Both fields are emptied as soon as the call returns, whatever the answer.
export function LoginForm({ onSubmit, notice }: Props) {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const t = useText();

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(undefined);
    const failure = await onSubmit(username, password);
    setUsername("");
    setPassword("");
    setBusy(false);
    setError(failure);
  }

  return (
    <section className="login" aria-label={t("logIn")}>
      <div className="bubble assistant">
        <p>{t("welcome")}</p>
        {notice && (
          <p className="notice" role="status">
            {notice}
          </p>
        )}
      </div>
      <form className="bubble form" onSubmit={submit}>
        <label>
          {t("username")}
          <input name="username" autoComplete="username" value={username} onChange={(e) => setUsername(e.target.value)} required />
        </label>
        <label>
          {t("password")}
          <input
            name="password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
        </label>
        {error && (
          <p className="error" role="alert">
            {error}
          </p>
        )}
        <button type="submit" disabled={busy}>
          {busy ? t("loggingIn") : t("logIn")}
        </button>
      </form>
      <div className="bubble assistant demo">
        <p className="demo-note" role="note">
          {t("demoNote")}
        </p>
        <dl className="test-logins" aria-label={t("testLogins")}>
          <dt>{t("admin")}</dt>
          <dd>
            {t("usernameWord")} <code>admin</code>, {t("passwordWord")} <code>admin</code>
          </dd>
          <dt>{t("customer")}</dt>
          <dd>
            {t("usernameWord")} <code>IMP-C001</code>, {t("passwordWord")} <code>IMP-C001</code> {t("anyCustomer")}
          </dd>
        </dl>
      </div>
      <Guide open />
    </section>
  );
}
