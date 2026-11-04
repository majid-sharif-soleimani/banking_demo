import { useText, type TextKey } from "./i18n";

const useSteps: TextKey[] = ["guideUseLogin", "guideUseCustomer", "guideUseAdmin", "guideUseConfirm", "guideUseLanguage", "guideUseOldChats"];
const limits: TextKey[] = ["guideLimitShared", "guideLimitPrivate", "guideLimitMistakes", "guideLimitSession", "guideLimitPace", "guideLimitMemory", "guideLimitUpload", "guideLimitOffline"];

// What the app is for, how to use it and its limits. Plain text of the UI: it is never a chat message, so it does not
// reach the model. Open on the login screen, the first thing a visitor sees; closed in a new chat, one click away.
export function Guide({ open }: { open: boolean }) {
  const t = useText();

  return (
    <details className="bubble assistant guide" open={open}>
      <summary>{t("guideTitle")}</summary>
      <h2>{t("guidePurposeTitle")}</h2>
      <p>{t("guidePurpose")}</p>
      <h2>{t("guideUseTitle")}</h2>
      <ul>
        {useSteps.map((key) => (
          <li key={key}>{t(key)}</li>
        ))}
      </ul>
      <h2>{t("guideLimitsTitle")}</h2>
      <ul>
        {limits.map((key) => (
          <li key={key}>{t(key)}</li>
        ))}
      </ul>
    </details>
  );
}
