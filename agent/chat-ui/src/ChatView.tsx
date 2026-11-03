import { useMemo, useState } from "react";
import { HttpAgent } from "@ag-ui/client";
import { AssistantRuntimeProvider, ComposerPrimitive, MessagePrimitive, ThreadPrimitive } from "@assistant-ui/react";
import { useAgUiRuntime } from "@assistant-ui/react-ag-ui";
import { CardTool, ChartTool, ConfirmationTool, DiagramTool, DocumentRequestTool, FormTool, TableTool } from "./components";
import { clientHeaders, me, noticeFor, sessionEndCodes, type SessionInfo } from "./api";
import { LanguageContext, useText } from "./i18n";
import { useContext } from "react";

type Props = {
  session: SessionInfo;
  onEnded: (code: string) => void;
};

function UserMessage() {
  return (
    <MessagePrimitive.Root className="bubble user" dir="auto">
      <MessagePrimitive.Parts />
    </MessagePrimitive.Root>
  );
}

// The tools the agent uses to show something, by name; any other tool call (none reach the browser today) is not drawn.
const toolComponents = {
  tools: {
    by_name: {
      show_table: TableTool,
      show_card: CardTool,
      show_chart: ChartTool,
      show_diagram: DiagramTool,
      show_form: FormTool,
      show_confirmation: ConfirmationTool,
      request_document: DocumentRequestTool,
    },
    Fallback: () => null,
  },
};

function AssistantMessage() {
  return (
    <MessagePrimitive.Root className="bubble assistant" dir="auto">
      <MessagePrimitive.Parts components={toolComponents} />
    </MessagePrimitive.Root>
  );
}

// One chat of one login. App mounts it with key={chatId}, so a new login always starts from an empty thread: nothing
// of an earlier chat, and nothing of the login form, is in this component's state.
export function ChatView({ session, onEnded }: Props) {
  // The agent talks to POST /agent on the same origin; fetch sends the session cookie itself. The thread id is only
  // a label: the host writes to the session's own chat whatever the client says.
  const agent = useMemo(() => new HttpAgent({ url: "/agent", headers: { ...clientHeaders }, threadId: session.chatId }), [session.chatId]);

  // A bank that does not answer is not the end of the session: the chat stays, with a note to try again.
  const [problem, setProblem] = useState<string>();
  const language = useContext(LanguageContext);
  const t = useText();

  const runtime = useAgUiRuntime({
    agent,
    showThinking: false,
    onError: (error) => {
      const code = (error as Error & { code?: unknown }).code;
      if (typeof code === "string" && sessionEndCodes.has(code)) {
        onEnded(code);
        return;
      }

      // Anything else (no code, or the bank down): ask the host what is left of the session.
      void me().then((answer) => {
        if (answer.ok) {
          setProblem(noticeFor("bank_unavailable", language));
        } else if (answer.code === "bank_unavailable") {
          setProblem(noticeFor(answer.code, language));
        } else {
          onEnded(answer.code);
        }
      });
    },
  });

  return (
    <AssistantRuntimeProvider runtime={runtime}>
      <ThreadPrimitive.Root className="thread">
        <ThreadPrimitive.Viewport className="messages">
          <ThreadPrimitive.Empty>
            <div className="bubble assistant" dir="auto">
              <p>{t("greeting", { name: session.name, role: t(session.role === "admin" ? "theAdmin" : "aCustomer") })}</p>
            </div>
          </ThreadPrimitive.Empty>
          <ThreadPrimitive.Messages components={{ UserMessage, AssistantMessage }} />
        </ThreadPrimitive.Viewport>
        {problem && (
          <p className="error" role="alert">
            {problem}
          </p>
        )}
        <ComposerPrimitive.Root className="composer" onSubmit={() => setProblem(undefined)}>
          <ComposerPrimitive.Input className="composer-input" placeholder={t("writeMessage")} aria-label={t("message")} dir="auto" />
          <ComposerPrimitive.Send className="composer-send">{t("send")}</ComposerPrimitive.Send>
        </ComposerPrimitive.Root>
      </ThreadPrimitive.Root>
    </AssistantRuntimeProvider>
  );
}
