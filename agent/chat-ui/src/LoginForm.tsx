import { useState, type FormEvent } from "react";

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
    <section className="login" aria-label="Log in">
      <div className="bubble assistant">
        <p>Welcome to the bank. Please log in to start a chat.</p>
        <p className="demo-note" role="note">
          This is a demo. All data here is test data, nothing is real, and changes have no operational effect.
        </p>
        <dl className="test-logins" aria-label="Test logins">
          <dt>Admin</dt>
          <dd>
            username <code>admin</code>, password <code>admin</code>
          </dd>
          <dt>Customer</dt>
          <dd>
            username <code>IMP-C001</code>, password <code>IMP-C001</code> (any customer: the national id as both)
          </dd>
        </dl>
        {notice && (
          <p className="notice" role="status">
            {notice}
          </p>
        )}
      </div>
      <form className="bubble form" onSubmit={submit}>
        <label>
          Username
          <input name="username" autoComplete="username" value={username} onChange={(e) => setUsername(e.target.value)} required />
        </label>
        <label>
          Password
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
          {busy ? "Logging in…" : "Log in"}
        </button>
      </form>
    </section>
  );
}
