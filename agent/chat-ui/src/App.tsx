import { useEffect, useState } from "react";
import { ChatView } from "./ChatView";
import { LoginForm } from "./LoginForm";
import { login, logout, me, noticeFor, type SessionInfo } from "./api";

type View = { kind: "loading" } | { kind: "login"; notice?: string } | { kind: "chat"; session: SessionInfo };

// Two screens: the login form, and the chat of the current login. Leaving the chat (logout or an ended session)
// always goes back to the login form, and a login always opens a new chat (phase 2 design plan, decisions A12).
export function App() {
  const [view, setView] = useState<View>({ kind: "loading" });

  useEffect(() => {
    void me().then((answer) =>
      setView(answer.ok ? { kind: "chat", session: answer.session } : { kind: "login", notice: noticeFor(answer.code) }),
    );
  }, []);

  async function submitLogin(username: string, password: string): Promise<string | undefined> {
    const answer = await login(username, password);
    if (answer.ok) {
      setView({ kind: "chat", session: answer.session });
      return undefined;
    }

    return answer.code === "invalid_credentials" ? "Wrong username or password." : (noticeFor(answer.code) ?? "Login failed. Try again.");
  }

  async function leave() {
    await logout();
    setView({ kind: "login", notice: noticeFor("logout") });
  }

  return (
    <div className="app">
      <header className="top">
        <h1>Bank Chat</h1>
        {view.kind === "chat" && (
          <div className="who">
            <span>
              {view.session.name} · {view.session.role}
            </span>
            <button type="button" onClick={() => void leave()}>
              Log out
            </button>
          </div>
        )}
      </header>
      <main>
        {view.kind === "loading" && <p className="loading">Loading…</p>}
        {view.kind === "login" && <LoginForm onSubmit={submitLogin} notice={view.notice} />}
        {view.kind === "chat" && (
          <ChatView
            key={view.session.chatId}
            session={view.session}
            onEnded={(code) => setView({ kind: "login", notice: noticeFor(code) ?? noticeFor("session_expired") })}
          />
        )}
      </main>
    </div>
  );
}
