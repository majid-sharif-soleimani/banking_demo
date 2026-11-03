import { render, screen, within } from "@testing-library/react";
import type { ComponentProps } from "react";
import { describe, expect, it } from "vitest";
import { CardTool, TableTool, type CardArgs, type TableArgs } from "./components";

// The components only read args; the rest of the tool-call props does not matter here.
function props<T>(args: T) {
  return { args } as unknown as ComponentProps<typeof TableTool> & ComponentProps<typeof CardTool>;
}

describe("TableTool", () => {
  const table: TableArgs = {
    title: "Your accounts",
    columns: [
      { label: "Account", type: "text" },
      { label: "Balance", type: "money" },
    ],
    rows: [["5555 0000 1", "1 200.00 SEK"]],
  };

  it("draws the title, the headings and the cells", () => {
    render(<TableTool {...props(table)} />);

    expect(screen.getByText("Your accounts")).toBeTruthy();
    const cells = within(screen.getByRole("table")).getAllByRole("cell").map((c) => c.textContent);
    expect(cells).toEqual(["5555 0000 1", "1 200.00 SEK"]);
    expect(screen.getByRole("columnheader", { name: "Balance" }).className).toBe("num");
  });

  it("shows text from the bank as text, never as HTML", () => {
    render(<TableTool {...props({ ...table, rows: [["<b>x</b>", "1 SEK"]] })} />);

    expect(screen.getByRole("table").querySelector("b")).toBeNull();
    expect(screen.getByText("<b>x</b>")).toBeTruthy();
  });

  it("says so when there are no rows", () => {
    render(<TableTool {...props({ ...table, rows: [] })} />);

    expect(screen.getByText("Nothing to show.")).toBeTruthy();
  });

  it("draws nothing broken while the arguments are still arriving", () => {
    render(<TableTool {...props(undefined)} />);

    expect(screen.getByRole("table")).toBeTruthy();
  });
});

describe("CardTool", () => {
  it("draws the labelled fields", () => {
    const card: CardArgs = { title: "Home loan", fields: [{ label: "Outstanding", value: "180 000.00 SEK" }] };

    render(<CardTool {...props(card)} />);

    const region = screen.getByRole("region", { name: "Home loan" });
    expect(within(region).getByText("Outstanding")).toBeTruthy();
    expect(within(region).getByText("180 000.00 SEK")).toBeTruthy();
  });
});
