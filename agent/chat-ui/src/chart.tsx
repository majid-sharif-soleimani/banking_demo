// A small line or bar chart drawn as SVG, without a chart library (phase 2 design plan, section 5). The agent sends
// labels and up to four series; the host checked that every series has one value per label. Colours are the four first
// categorical slots of a colour-blind-checked palette (styles.css, --series-1..4). Identity is never colour alone: two
// or more series get a legend, every mark has a hover title, and "Show the numbers" opens the same data as a table.

export type ChartArgs = {
  title: string;
  kind: "line" | "bar";
  labels: string[];
  series: { name: string; values: number[] }[];
  unit: string;
};

const width = 600;
const height = 260;
const pad = { top: 12, right: 12, bottom: 28, left: 56 };

export function Chart({ args }: { args?: Partial<ChartArgs> }) {
  const labels = args?.labels ?? [];
  const series = (args?.series ?? []).filter((s) => Array.isArray(s.values)).slice(0, 4);
  const values = series.flatMap((s) => s.values);
  if (labels.length === 0 || values.length === 0) {
    return <figure className="component">{args?.title && <figcaption>{args.title}</figcaption>}</figure>;
  }

  // One axis that always includes zero, so bar lengths are honest.
  const max = Math.max(0, ...values);
  const min = Math.min(0, ...values);
  const span = max - min || 1;
  const plotW = width - pad.left - pad.right;
  const plotH = height - pad.top - pad.bottom;
  const y = (v: number) => pad.top + ((max - v) / span) * plotH;
  const step = plotW / labels.length;
  const x = (i: number) => pad.left + step * i + step / 2;
  const ticks = [min, min + span / 2, max];
  const format = (v: number) => `${v.toLocaleString(undefined, { maximumFractionDigits: 2 })} ${args?.unit ?? ""}`.trim();
  const barW = Math.max(2, Math.min(24, (step * 0.7) / series.length - 2));

  return (
    <figure className="component chart">
      {args?.title && <figcaption>{args.title}</figcaption>}
      {series.length > 1 && (
        <ul className="legend">
          {series.map((s, i) => (
            <li key={i}>
              <span className={`swatch s${i + 1}`} aria-hidden="true" />
              {s.name}
            </li>
          ))}
        </ul>
      )}
      <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label={args?.title ?? "Chart"}>
        {ticks.map((t, i) => (
          <g key={i} className="grid">
            <line x1={pad.left} x2={width - pad.right} y1={y(t)} y2={y(t)} />
            <text x={pad.left - 6} y={y(t)} textAnchor="end" dominantBaseline="middle">
              {t.toLocaleString(undefined, { maximumFractionDigits: 0 })}
            </text>
          </g>
        ))}
        {labels.map((label, i) => (
          <text key={i} className="axis-label" x={x(i)} y={height - 8} textAnchor="middle">
            {label}
          </text>
        ))}
        {series.map((s, si) =>
          args?.kind === "line" ? (
            <g key={si} className={`series s${si + 1}`}>
              <polyline fill="none" points={s.values.map((v, i) => `${x(i)},${y(v)}`).join(" ")} />
              {s.values.map((v, i) => (
                <circle key={i} cx={x(i)} cy={y(v)} r={4}>
                  <title>{`${s.name}, ${labels[i]}: ${format(v)}`}</title>
                </circle>
              ))}
            </g>
          ) : (
            <g key={si} className={`series s${si + 1}`}>
              {s.values.map((v, i) => (
                <rect
                  key={i}
                  x={x(i) - (series.length * (barW + 2)) / 2 + si * (barW + 2)}
                  y={Math.min(y(v), y(0))}
                  width={barW}
                  height={Math.max(1, Math.abs(y(v) - y(0)))}
                  rx={2}
                >
                  <title>{`${s.name}, ${labels[i]}: ${format(v)}`}</title>
                </rect>
              ))}
            </g>
          ),
        )}
      </svg>
      <details>
        <summary>Show the numbers</summary>
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th />
                {series.map((s, i) => (
                  <th key={i} className="num">
                    {s.name}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {labels.map((label, li) => (
                <tr key={li}>
                  <th scope="row">{label}</th>
                  {series.map((s, si) => (
                    <td key={si} className="num">
                      {s.values[li] === undefined ? "" : format(s.values[li])}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </figure>
  );
}
