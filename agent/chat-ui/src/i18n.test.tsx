import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { noticeFor } from "./api";
import { applyLanguage, initialLanguage, text } from "./i18n";

const { App } = await import("./App");

describe("languages", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
    applyLanguage("en");
  });

  it("fills placeholders in every language", () => {
    expect(text("en", "greeting", { name: "Anna", role: "a customer" })).toBe("Hello Anna. You are logged in as a customer. How can I help?");
    expect(text("sv", "iUploaded", { file: "lön.pdf" })).toBe("Jag har laddat upp lön.pdf.");
    expect(text("fa", "iUploaded", { file: "payslip.pdf" })).toContain("payslip.pdf");
  });

  it("gives the session notices in the chosen language", () => {
    expect(noticeFor("logout", "sv")).toBe("Du har loggat ut.");
    expect(noticeFor("logout")).toBe("You logged out.");
  });

  it("starts from the saved choice, else English for an unknown browser language", () => {
    expect(initialLanguage()).toBe("en");
    localStorage.setItem("bank-chat-language", "fa");
    expect(initialLanguage()).toBe("fa");
  });

  it("switching to Persian translates the login and turns the page right to left", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: "missing_session" }), { status: 401 })));
    render(<App />);
    await screen.findByLabelText("Username");

    await userEvent.selectOptions(screen.getByLabelText("Language"), "fa");

    expect(await screen.findByLabelText("نام کاربری")).toBeTruthy();
    expect(screen.getByRole("button", { name: "ورود" })).toBeTruthy();
    expect(document.documentElement.dir).toBe("rtl");
    expect(document.documentElement.lang).toBe("fa");
    expect(localStorage.getItem("bank-chat-language")).toBe("fa");
  });
});
