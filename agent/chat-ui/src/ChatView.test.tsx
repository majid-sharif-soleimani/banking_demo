import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ChatView } from "./ChatView";

// The real AG-UI client and runtime over a fake POST /agent: what the person reads when a run fails.
function failingRun(code: string): Response {
  const events = [
    { type: "RUN_STARTED", threadId: "c-1", runId: "r-1" },
    { type: "RUN_ERROR", code, message: "From the host." },
  ];
  return new Response(events.map((e) => `data: ${JSON.stringify(e)}\n\n`).join(""), {
    status: 200,
    headers: { "Content-Type": "text/event-stream" },
  });
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("a failed run", () => {
  it.each([
    ["model_unavailable", "assistant is not available"],
    ["no_answer", "stopped early"],
    ["run_budget", "too much work"],
  ])("%s shows its own note, not the bank's", async (code, note) => {
    vi.stubGlobal("fetch", vi.fn().mockImplementation(async () => failingRun(code)));
    Element.prototype.scrollTo = vi.fn();
    vi.stubGlobal("ResizeObserver", class { observe() {} unobserve() {} disconnect() {} });
    render(<ChatView session={{ chatId: "c-1", username: "admin", role: "admin", name: "Admin" } as never} onEnded={vi.fn()} />);

    await userEvent.type(screen.getByRole("textbox"), "Hello{Enter}");

    expect(await screen.findByText(new RegExp(note), {}, { timeout: 3000 })).toBeTruthy();
    expect(screen.queryByText(/could not be reached/)).toBeNull();
  });
});
