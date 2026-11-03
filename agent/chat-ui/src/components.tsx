import { useAui, type ToolCallMessagePartComponent } from "@assistant-ui/react";
import { Chart, type ChartArgs } from "./chart";
import { Diagram, type DiagramArgs } from "./diagram";
import { ConfirmationView, DocumentRequestView, FormView, type ConfirmationArgs, type DocumentRequestArgs, type FormArgs } from "./interactive";

// UI components as tools (phase 2 design plan, section 5, decision A8). The agent cannot draw: to show a table it calls
// the tool show_table, and the tool's arguments are the table. The agent host streams that call to the browser as an
// AG-UI tool call, and assistant-ui hands it to the component registered for the tool's name (see ChatView). The data
// was checked by the host before it was sent; here it is only drawn. React escapes every text, so a cell can never
// become HTML.

export type TableArgs = {
  title: string;
  columns: { label: string; type: "text" | "money" | "date" | "number" }[];
  rows: string[][];
};

export type CardArgs = {
  title: string;
  fields: { label: string; value: string }[];
};

// While the arguments are still arriving they can be incomplete; draw only what is already there.
export const TableTool: ToolCallMessagePartComponent<TableArgs> = ({ args }) => <Table args={args} />;

export const CardTool: ToolCallMessagePartComponent<CardArgs> = ({ args }) => <Card args={args} />;

export function Table({ args }: { args?: Partial<TableArgs> }) {
  const columns = args?.columns ?? [];
  const rows = args?.rows ?? [];
  return (
    <figure className="component">
      {args?.title && <figcaption>{args.title}</figcaption>}
      <div className="table-scroll">
        <table>
          <thead>
            <tr>
              {columns.map((column, i) => (
                <th key={i} className={alignOf(column.type)} dir="auto">
                  {column.label}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, r) => (
              <tr key={r}>
                {row.map((cell, c) => (
                  <td key={c} className={alignOf(columns[c]?.type)} dir="auto">
                    {cell}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {rows.length === 0 && <p className="notice">Nothing to show.</p>}
    </figure>
  );
}

export const Card = ({ args }: { args?: Partial<CardArgs> }) => (
  <section className="component card" aria-label={args?.title}>
    {args?.title && <h2>{args.title}</h2>}
    <dl>
      {(args?.fields ?? []).map((field, i) => (
        <div key={i} className="field">
          <dt dir="auto">{field.label}</dt>
          <dd dir="auto">{field.value}</dd>
        </div>
      ))}
    </dl>
  </section>
);

export const ChartTool: ToolCallMessagePartComponent<ChartArgs> = ({ args }) => <Chart args={args} />;

export const DiagramTool: ToolCallMessagePartComponent<DiagramArgs> = ({ args }) => <Diagram args={args} />;

// The answered components send a short chat message when the person is done, so the agent goes on (useAui is the
// assistant-ui client of the current thread).
export const FormTool: ToolCallMessagePartComponent<FormArgs> = ({ args }) => {
  const aui = useAui();
  return <FormView args={args} onDone={(text) => aui.thread.append(text)} />;
};

export const ConfirmationTool: ToolCallMessagePartComponent<ConfirmationArgs> = ({ args }) => {
  const aui = useAui();
  return <ConfirmationView args={args} onDone={(text) => aui.thread.append(text)} />;
};

export const DocumentRequestTool: ToolCallMessagePartComponent<DocumentRequestArgs> = ({ args }) => {
  const aui = useAui();
  return <DocumentRequestView args={args} onDone={(text) => aui.thread.append(text)} />;
};

// Money and numbers line up on the right, so amounts can be compared down a column.
function alignOf(type: string | undefined) {
  return type === "money" || type === "number" ? "num" : undefined;
}
