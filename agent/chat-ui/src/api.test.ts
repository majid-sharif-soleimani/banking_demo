import { afterEach, describe, expect, it, vi } from "vitest";
import { login, noticeFor } from "./api";

describe("api", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("sends the chat UI header with the login", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ role: "admin", name: "Admin", chatId: "c1" }), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    const answer = await login("admin", "admin");

    expect(answer).toEqual({ ok: true, session: { role: "admin", name: "Admin", chatId: "c1" } });
    const init = fetchMock.mock.calls[0][1] as RequestInit;
    expect((init.headers as Record<string, string>)["X-Bank-Chat"]).toBe("1");
  });

  it("returns the host's problem code", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: "invalid_credentials" }), { status: 401 })));

    expect(await login("a", "b")).toEqual({ ok: false, code: "invalid_credentials" });
  });

  it("treats a network failure as an unreachable bank", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));

    expect(await login("a", "b")).toEqual({ ok: false, code: "bank_unavailable" });
  });

  it.each([
    ["session_expired", "nothing happened for a while"],
    ["session_replaced", "logged in somewhere else"],
    ["bank_unavailable", "could not be reached"],
    ["no_answer", "stopped early"],
    ["run_budget", "too much work"],
    ["model_unavailable", "assistant is not available"],
  ])("explains %s", (code, text) => {
    expect(noticeFor(code)).toContain(text);
  });

  it("says nothing for a plain missing session", () => {
    expect(noticeFor("missing_session")).toBeUndefined();
  });
});
