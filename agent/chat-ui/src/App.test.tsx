import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { SessionInfo } from "./api";

// The chat itself needs a browser runtime; here it only has to show which session it was given.
vi.mock("./ChatView", () => ({
  ChatView: ({ session }: { session: SessionInfo }) => <div data-testid="chat">chat {session.chatId}</div>,
}));

const { App } = await import("./App");

function respond(status: number, body: unknown) {
  return new Response(JSON.stringify(body), { status });
}

describe("App", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("shows the reason when the session ended while the page was closed", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(respond(401, { code: "session_replaced" })));

    render(<App />);

    expect((await screen.findByRole("status")).textContent).toContain("logged in somewhere else");
  });

  it("opens a new chat after a login and leaves no login field on the page", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(respond(401, { code: "missing_session" }))
      .mockResolvedValueOnce(respond(200, { role: "customer", name: "Anna", chatId: "chat-2" }));
    vi.stubGlobal("fetch", fetchMock);
    render(<App />);

    await userEvent.type(await screen.findByLabelText("Username"), "IMP-C001");
    await userEvent.type(screen.getByLabelText("Password"), "IMP-C001");
    await userEvent.click(screen.getByRole("button", { name: "Log in" }));

    expect((await screen.findByTestId("chat")).textContent).toBe("chat chat-2");
    expect(screen.queryByLabelText("Password")).toBeNull();
    expect(document.body.innerHTML).not.toContain("IMP-C001");
  });

  it("goes back to the login form after a logout", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(respond(200, { role: "admin", name: "Admin", chatId: "chat-1" }))
        .mockResolvedValueOnce(new Response(null, { status: 204 })),
    );
    render(<App />);

    await userEvent.click(await screen.findByRole("button", { name: "Log out" }));

    expect(await screen.findByLabelText("Username")).toBeTruthy();
    expect(screen.getByRole("status").textContent).toBe("You logged out.");
  });
});
