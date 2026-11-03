import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { LoginForm } from "./LoginForm";

describe("LoginForm", () => {
  it("sends the values to its handler and empties both fields afterwards", async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);
    render(<LoginForm onSubmit={onSubmit} />);

    await userEvent.type(screen.getByLabelText("Username"), "IMP-C001");
    await userEvent.type(screen.getByLabelText("Password"), "IMP-C001");
    await userEvent.click(screen.getByRole("button", { name: "Log in" }));

    expect(onSubmit).toHaveBeenCalledWith("IMP-C001", "IMP-C001");
    expect(screen.getByLabelText("Username")).toHaveProperty("value", "");
    expect(screen.getByLabelText("Password")).toHaveProperty("value", "");
  });

  it("empties the fields after a refused login too and shows the error", async () => {
    render(<LoginForm onSubmit={vi.fn().mockResolvedValue("Wrong username or password.")} />);

    await userEvent.type(screen.getByLabelText("Username"), "admin");
    await userEvent.type(screen.getByLabelText("Password"), "nope");
    await userEvent.click(screen.getByRole("button", { name: "Log in" }));

    expect(screen.getByRole("alert").textContent).toBe("Wrong username or password.");
    expect(screen.getByLabelText("Password")).toHaveProperty("value", "");
  });

  it("shows why the person is back at the login", () => {
    render(<LoginForm onSubmit={vi.fn()} notice="You logged in somewhere else." />);

    expect(screen.getByRole("status").textContent).toBe("You logged in somewhere else.");
  });

  it("keeps the password hidden", () => {
    render(<LoginForm onSubmit={vi.fn()} />);

    expect(screen.getByLabelText("Password").getAttribute("type")).toBe("password");
  });
});
