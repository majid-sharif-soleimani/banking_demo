import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Chart } from "./chart";
import { Stored } from "./History";
import { ConfirmationView, DocumentRequestView, FormView, type FormArgs } from "./interactive";

function respond(status: number, body: unknown) {
  return new Response(JSON.stringify(body), { status });
}

afterEach(() => vi.unstubAllGlobals());

describe("ConfirmationView", () => {
  const args = { changeId: "c-1", title: "Transfer money", fields: [{ label: "Amount", value: "250.00" }] };

  it("shows the exact values, confirms through the host and tells the chat", async () => {
    const fetchMock = vi.fn().mockResolvedValue(respond(200, { status: "done", message: "{}" }));
    vi.stubGlobal("fetch", fetchMock);
    const onDone = vi.fn();
    render(<ConfirmationView args={args} onDone={onDone} />);

    expect(screen.getByText("250.00")).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Confirm" }));

    expect(fetchMock.mock.calls[0][0]).toBe("/agent/changes/c-1/confirm");
    expect((fetchMock.mock.calls[0][1] as RequestInit).headers).toMatchObject({ "X-Bank-Chat": "1" });
    expect(await screen.findByRole("status")).toBeTruthy();
    expect(onDone).toHaveBeenCalledWith("I confirmed the change.");
    expect(screen.queryByRole("button", { name: "Confirm" })).toBeNull();
  });

  it("says so when the bank refused the confirmed change", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(respond(200, { status: "failed", message: "insufficient_funds" })));
    render(<ConfirmationView args={args} onDone={vi.fn()} />);

    await userEvent.click(screen.getByRole("button", { name: "Confirm" }));

    expect((await screen.findByRole("alert")).textContent).toContain("insufficient_funds");
  });

  it("cancels", async () => {
    const fetchMock = vi.fn().mockResolvedValue(respond(200, { status: "cancelled" }));
    vi.stubGlobal("fetch", fetchMock);
    const onDone = vi.fn();
    render(<ConfirmationView args={args} onDone={onDone} />);

    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(fetchMock.mock.calls[0][0]).toBe("/agent/changes/c-1/cancel");
    expect(onDone).toHaveBeenCalledWith("I cancelled the change.");
  });

  it("has no buttons in an old chat", () => {
    render(<ConfirmationView args={args} readOnly />);

    expect(screen.queryByRole("button")).toBeNull();
  });
});

describe("FormView", () => {
  const args: FormArgs = {
    formId: "f-1",
    title: "Transfer",
    submitLabel: "Continue",
    fields: [
      { name: "amount", label: "Amount", type: "number", required: true, min: 1, max: 1000 },
      { name: "frequency", label: "How often", type: "choice", required: false, options: ["Weekly", "Monthly"] },
    ],
  };

  it("sends the values to the host, not to the chat, and then tells the chat", async () => {
    const fetchMock = vi.fn().mockResolvedValue(respond(200, { status: "done" }));
    vi.stubGlobal("fetch", fetchMock);
    const onDone = vi.fn();
    render(<FormView args={args} onDone={onDone} />);

    await userEvent.type(screen.getByLabelText(/Amount/), "250");
    await userEvent.selectOptions(screen.getByLabelText(/How often/), "Monthly");
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));

    expect(fetchMock.mock.calls[0][0]).toBe("/agent/forms/f-1");
    expect(JSON.parse((fetchMock.mock.calls[0][1] as RequestInit).body as string)).toEqual({ values: { amount: "250", frequency: "Monthly" } });
    expect(onDone).toHaveBeenCalledWith("I sent the form.");
  });

  it("shows the host's errors next to the fields and stays open", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(respond(400, { code: "invalid_values", errors: { amount: "At most 1000." } })));
    render(<FormView args={args} onDone={vi.fn()} />);

    await userEvent.type(screen.getByLabelText(/Amount/), "999");
    await userEvent.click(screen.getByRole("button", { name: "Continue" }));

    expect((await screen.findByRole("alert")).textContent).toBe("At most 1000.");
    expect(screen.getByRole("button", { name: "Continue" })).toHaveProperty("disabled", false);
  });
});

describe("DocumentRequestView", () => {
  it("uploads the chosen file as multipart form data", async () => {
    const fetchMock = vi.fn().mockResolvedValue(respond(200, { status: "done" }));
    vi.stubGlobal("fetch", fetchMock);
    const onDone = vi.fn();
    const { container } = render(<DocumentRequestView args={{ requestId: "d-1", applicationId: "a", description: "Your payslip" }} onDone={onDone} />);

    await userEvent.upload(container.querySelector("input[type=file]") as HTMLInputElement, new File(["%PDF"], "payslip.pdf", { type: "application/pdf" }));
    await userEvent.click(screen.getByRole("button", { name: "Upload" }));

    expect(fetchMock.mock.calls[0][0]).toBe("/agent/documents/d-1");
    expect((fetchMock.mock.calls[0][1] as RequestInit).body).toBeInstanceOf(FormData);
    expect(onDone).toHaveBeenCalledWith("I uploaded payslip.pdf.");
  });
});

describe("Chart", () => {
  const args = { title: "Spending", kind: "bar" as const, labels: ["Oct", "Nov"], series: [{ name: "Food", values: [100, 250] }, { name: "Rent", values: [900, 900] }], unit: "SEK" };

  it("draws one bar per value with a hover title, a legend for two series and the numbers as a table", () => {
    const { container } = render(<Chart args={args} />);

    expect(container.querySelectorAll("rect")).toHaveLength(4);
    expect(container.querySelector("rect title")?.textContent).toBe("Food, Oct: 100 SEK");
    expect(container.querySelector(".legend")?.textContent).toBe("FoodRent");
    expect(screen.getByText("Show the numbers")).toBeTruthy();
  });

  it("draws a line with a point per value", () => {
    const { container } = render(<Chart args={{ ...args, kind: "line", series: [args.series[0]] }} />);

    expect(container.querySelectorAll("polyline")).toHaveLength(1);
    expect(container.querySelectorAll("circle")).toHaveLength(2);
    expect(container.querySelector(".legend")).toBeNull();
  });
});

describe("Stored", () => {
  it("draws a stored table and a person's confirmed change", () => {
    render(
      <>
        <Stored message={{ author: "assistant", kind: "table", content: JSON.stringify({ title: "Accounts", columns: [{ label: "A", type: "text" }], rows: [["x"]] }), createdAt: "" }} />
        <Stored message={{ author: "user", kind: "change_result", content: JSON.stringify({ title: "Transfer money", status: "done" }), createdAt: "" }} />
      </>,
    );

    expect(screen.getByRole("table")).toBeTruthy();
    expect(screen.getByText("Confirmed: Transfer money.")).toBeTruthy();
  });
});
