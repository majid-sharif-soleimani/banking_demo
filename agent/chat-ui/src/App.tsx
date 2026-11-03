import { useEffect, useState } from "react";
import { ChatView } from "./ChatView";
import { History } from "./History";
import { LoginForm } from "./LoginForm";
import { login, logout, me, noticeFor, type SessionInfo } from "./api";
import { applyLanguage, initialLanguage, LanguageContext, languages, text, type Language } from "./i18n";

type View =
  | { kind: "loading" }
  | { kind: "login"; notice?: string }
  | { kind: "chat"; session: SessionInfo };

// Two screens: the login form, and the chat of the current login. Leaving the chat (logout or an ended session)
// always goes back to the login form, and a login always opens a new chat (phase 2 design plan, decisions A12).
export function App() {
  const [view, setView] = useState<View>({ kind: "loading" });
  // The old chats open over the chat; the chat stays mounted underneath, so nothing typed is lost.
  const [showHistory, setShowHistory] = useState(false);
  const [language, setLanguage] = useState<Language>(initialLanguage);
  const t = (key: Parameters<typeof text>[1]) => text(language, key);

  useEffect(() => applyLanguage(language), [language]);

  useEffect(() => {
    void me().then((answer) =>
      setView(
        answer.ok
          ? { kind: "chat", session: answer.session }
          : { kind: "login", notice: noticeFor(answer.code, language) },
      ),
    );
    // Only on the first render: the session check runs once, in the language picked at that moment.
  }, []);

  async function submitLogin(
    username: string,
    password: string,
  ): Promise<string | undefined> {
    const answer = await login(username, password);
    if (answer.ok) {
      setView({ kind: "chat", session: answer.session });
      return undefined;
    }

    return answer.code === "invalid_credentials"
      ? t("wrongCredentials")
      : (noticeFor(answer.code, language) ?? t("loginFailed"));
  }

  async function leave() {
    setShowHistory(false);
    await logout();
    setView({ kind: "login", notice: noticeFor("logout", language) });
  }

  return (
    <LanguageContext.Provider value={language}>
    <div className="app">
      <header className="top">
        <h1>{t("appTitle")}</h1>
        <div className="who">
          <select aria-label={t("language")} value={language} onChange={(e) => setLanguage(e.target.value as Language)}>
            {languages.map((l) => (
              <option key={l.code} value={l.code} lang={l.code}>
                {l.name}
              </option>
            ))}
          </select>
        {view.kind === "chat" && (
          <>
            <span>
              {view.session.name} · {t(view.session.role === "admin" ? "roleAdmin" : "roleCustomer")}
            </span>
            <button
              type="button"
              className="secondary"
              onClick={() => setShowHistory(!showHistory)}
            >
              {showHistory ? t("chat") : t("oldChats")}
            </button>
            <button type="button" onClick={() => void leave()}>
              {t("logOut")}
            </button>
          </>
        )}
        </div>
      </header>
      <main>
        {view.kind === "loading" && <p className="loading">{t("loading")}</p>}
        {view.kind === "login" && (
          <LoginForm onSubmit={submitLogin} notice={view.notice} />
        )}
        {view.kind === "chat" && showHistory && (
          <History
            isAdmin={view.session.role === "admin"}
            onBack={() => setShowHistory(false)}
          />
        )}
        {view.kind === "chat" && (
          <div hidden={showHistory} className="chat-slot">
            <ChatView
              key={view.session.chatId}
              session={view.session}
              onEnded={(code) => {
                setShowHistory(false);
                setView({
                  kind: "login",
                  notice: noticeFor(code, language) ?? noticeFor("session_expired", language),
                });
              }}
            />
          </div>
        )}
      </main>
    </div>
    </LanguageContext.Provider>
  );
}
