// The agent host's /auth endpoints (backend for frontend, phase 2 design plan decision A9). The browser never sees the
// bank token: it holds only the HttpOnly session cookie, which fetch sends on same-origin calls by itself.

export type SessionInfo = {
  role: "customer" | "admin";
  name: string;
  chatId: string;
};

export type Answer = { ok: true; session: SessionInfo } | { ok: false; code: string };

// Every state-changing call carries this header; the host refuses calls without it (ChatClientHeader).
export const clientHeaders = { "X-Bank-Chat": "1" } as const;

async function problemCode(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { code?: unknown };
    return typeof body.code === "string" ? body.code : `http_${response.status}`;
  } catch {
    return `http_${response.status}`;
  }
}

async function sessionAnswer(response: Response): Promise<Answer> {
  return response.ok
    ? { ok: true, session: (await response.json()) as SessionInfo }
    : { ok: false, code: await problemCode(response) };
}

export async function login(username: string, password: string): Promise<Answer> {
  try {
    const response = await fetch("/auth/login", {
      method: "POST",
      headers: { ...clientHeaders, "Content-Type": "application/json" },
      body: JSON.stringify({ username, password }),
    });
    return await sessionAnswer(response);
  } catch {
    return { ok: false, code: "bank_unavailable" };
  }
}

export async function logout(): Promise<void> {
  try {
    await fetch("/auth/logout", { method: "POST", headers: clientHeaders });
  } catch {
    // The host forgets the session anyway after its idle timeout.
  }
}

export async function me(): Promise<Answer> {
  try {
    return await sessionAnswer(await fetch("/auth/me"));
  } catch {
    return { ok: false, code: "bank_unavailable" };
  }
}

// Why the person sees the login form again. Codes come from the host (SessionEnd.Code, Problems).
export function noticeFor(code: string): string | undefined {
  switch (code) {
    case "session_expired":
      return "Your session ended because nothing happened for a while. Please log in again.";
    case "session_replaced":
      return "You logged in somewhere else, so this chat was closed. Please log in again.";
    case "new_login":
      return "A newer login closed this chat.";
    case "logout":
      return "You logged out.";
    case "bank_unavailable":
      return "The bank could not be reached. Try again in a moment.";
    default:
      return undefined;
  }
}

export const sessionEndCodes = new Set(["session_expired", "session_replaced", "new_login", "chat_closed", "missing_session"]);
