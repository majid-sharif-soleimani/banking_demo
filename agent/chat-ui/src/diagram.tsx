import { useEffect, useId, useState } from "react";

// A Mermaid diagram (phase 2 design plan, section 5). Mermaid is large, so it loads only when the first diagram
// appears. securityLevel "strict" makes Mermaid escape the text and drop links and scripts: the diagram text comes from
// the model and must never become active HTML.

export type DiagramArgs = { title: string; mermaid: string };

export function Diagram({ args }: { args?: Partial<DiagramArgs> }) {
  const id = useId().replace(/[^a-zA-Z0-9]/g, "");
  const [svg, setSvg] = useState<string>();
  const [failed, setFailed] = useState(false);
  const text = args?.mermaid;

  useEffect(() => {
    if (!text) {
      return;
    }

    let active = true;
    void import("mermaid")
      .then(async ({ default: mermaid }) => {
        const dark = window.matchMedia?.("(prefers-color-scheme: dark)").matches ?? false;
        mermaid.initialize({ startOnLoad: false, securityLevel: "strict", theme: dark ? "dark" : "neutral" });
        const result = await mermaid.render(`diagram${id}`, text);
        if (active) {
          setSvg(result.svg);
        }
      })
      .catch(() => active && setFailed(true));
    return () => {
      active = false;
    };
  }, [id, text]);

  return (
    <figure className="component diagram">
      {args?.title && <figcaption>{args.title}</figcaption>}
      {/* Mermaid's own output after its strict sanitising; nothing else is set as HTML anywhere in the UI. */}
      {svg && !failed && <div className="diagram-svg" dangerouslySetInnerHTML={{ __html: svg }} />}
      {(failed || !svg) && text && <pre className="diagram-text">{text}</pre>}
    </figure>
  );
}
