import { createContext, useContext } from "react";

// The UI's own texts in the three languages of the bank (phase 2 design plan, business question 5): English, Swedish
// and Persian. The agent's answers follow the language of the person's message; these texts follow the language the
// person picks in the header (remembered in this browser; the first time, the browser's language).
//
// Persian is written right to left. Setting dir="rtl" on <html> turns the whole page around: the CSS uses logical
// properties (start/end instead of left/right), so the layout mirrors by itself. Messages keep dir="auto", so an
// English answer in a Persian UI still reads left to right.

export type Language = "en" | "sv" | "fa";

export const languages: { code: Language; name: string }[] = [
  { code: "en", name: "English" },
  { code: "sv", name: "Svenska" },
  { code: "fa", name: "فارسی" },
];

const en = {
  appTitle: "Bank Chat",
  language: "Language",
  roleAdmin: "admin",
  roleCustomer: "customer",
  chat: "Chat",
  oldChats: "Old chats",
  logOut: "Log out",
  loading: "Loading…",
  wrongCredentials: "Wrong username or password.",
  loginFailed: "Login failed. Try again.",
  noticeSessionExpired: "Your session ended because nothing happened for a while. Please log in again.",
  noticeSessionReplaced: "You logged in somewhere else, so this chat was closed. Please log in again.",
  noticeNewLogin: "A newer login closed this chat.",
  noticeLogout: "You logged out.",
  noticeBankUnavailable: "The bank could not be reached. Try again in a moment.",
  noticeTooManyRuns: "You sent many messages in a short time. Wait a minute and try again.",
  noticeRunInProgress: "Another message is still being answered. Try again in a moment.",
  noticeNoAnswer: "The answer stopped early. Ask for a smaller part, for example one page of a list.",
  logIn: "Log in",
  loggingIn: "Logging in…",
  welcome: "Welcome to the bank. Please log in to start a chat.",
  username: "Username",
  password: "Password",
  demoNote: "This is a demo. All data here is test data, nothing is real, and changes have no operational effect.",
  testLogins: "Test logins",
  admin: "Admin",
  customer: "Customer",
  usernameWord: "username",
  passwordWord: "password",
  anyCustomer: "(any customer: the national id as both)",
  greeting: "Hello {name}. You are logged in as {role}. How can I help?",
  theAdmin: "the admin",
  aCustomer: "a customer",
  writeMessage: "Write a message",
  message: "Message",
  send: "Send",
  sent: "Sent",
  formGone: "This form can no longer be sent. Ask again in the chat.",
  iSentTheForm: "I sent the form.",
  confirm: "Confirm",
  cancel: "Cancel",
  sending: "Sending…",
  done: "Done.",
  cancelledNothingChanged: "Cancelled. Nothing was changed.",
  bankRefused: "The bank did not make the change",
  changeGone: "This change can no longer be confirmed. Ask again in the chat.",
  bankNoAnswer: "The bank did not answer, so the change may or may not have been made. Press Confirm again: the bank makes it only once.",
  iConfirmed: "I confirmed the change.",
  iCancelled: "I cancelled the change.",
  uploadDocument: "Upload a document",
  upload: "Upload",
  uploaded: "Uploaded.",
  uploadFailed: "The upload failed. Use one PDF, PNG or JPEG of at most 5 MB, and ask again in the chat.",
  uploadNoAnswer: "The bank did not answer. Upload the file again: the bank stores it only once.",
  iUploaded: "I uploaded {file}.",
  backToChat: "Back to the chat",
  backToList: "Back to the list",
  noChats: "No chats yet.",
  noQuestion: "(no question)",
  open: "open",
  closed: "closed",
  readOnly: "read-only",
  sentTheForm: "Sent the form “{title}”.",
  cancelled: "Cancelled",
  confirmed: "Confirmed",
  confirmedButRefused: "Confirmed, but the bank refused",
  confirmedNoAnswer: "Confirmed, but the bank did not answer",
  uploadedFile: "Uploaded {file} ({status}).",
  aDocument: "a document",
  chartLabel: "Chart",
  showNumbers: "Show the numbers",
};

export type TextKey = keyof typeof en;

const sv: Record<TextKey, string> = {
  appTitle: "Bankchatt",
  language: "Språk",
  roleAdmin: "administratör",
  roleCustomer: "kund",
  chat: "Chatt",
  oldChats: "Tidigare chattar",
  logOut: "Logga ut",
  loading: "Laddar…",
  wrongCredentials: "Fel användarnamn eller lösenord.",
  loginFailed: "Inloggningen misslyckades. Försök igen.",
  noticeSessionExpired: "Din session avslutades eftersom inget hände på en stund. Logga in igen.",
  noticeSessionReplaced: "Du loggade in någon annanstans, så den här chatten stängdes. Logga in igen.",
  noticeNewLogin: "En nyare inloggning stängde den här chatten.",
  noticeLogout: "Du har loggat ut.",
  noticeBankUnavailable: "Banken gick inte att nå. Försök igen om en stund.",
  noticeTooManyRuns: "Du har skickat många meddelanden på kort tid. Vänta en minut och försök igen.",
  noticeRunInProgress: "Ett annat meddelande besvaras fortfarande. Försök igen om en stund.",
  noticeNoAnswer: "Svaret avbröts i förtid. Be om en mindre del, till exempel en sida av en lista.",
  logIn: "Logga in",
  loggingIn: "Loggar in…",
  welcome: "Välkommen till banken. Logga in för att starta en chatt.",
  username: "Användarnamn",
  password: "Lösenord",
  demoNote: "Det här är en demo. All data är testdata, inget är på riktigt och ändringar får ingen verklig effekt.",
  testLogins: "Testinloggningar",
  admin: "Administratör",
  customer: "Kund",
  usernameWord: "användarnamn",
  passwordWord: "lösenord",
  anyCustomer: "(vilken kund som helst: personnumret som båda)",
  greeting: "Hej {name}. Du är inloggad som {role}. Hur kan jag hjälpa dig?",
  theAdmin: "administratör",
  aCustomer: "kund",
  writeMessage: "Skriv ett meddelande",
  message: "Meddelande",
  send: "Skicka",
  sent: "Skickat",
  formGone: "Formuläret kan inte längre skickas. Fråga igen i chatten.",
  iSentTheForm: "Jag har skickat formuläret.",
  confirm: "Bekräfta",
  cancel: "Avbryt",
  sending: "Skickar…",
  done: "Klart.",
  cancelledNothingChanged: "Avbrutet. Inget ändrades.",
  bankRefused: "Banken genomförde inte ändringen",
  changeGone: "Ändringen kan inte längre bekräftas. Fråga igen i chatten.",
  bankNoAnswer: "Banken svarade inte, så ändringen kan ha gjorts eller inte. Tryck på Bekräfta igen: banken gör den bara en gång.",
  iConfirmed: "Jag har bekräftat ändringen.",
  iCancelled: "Jag har avbrutit ändringen.",
  uploadDocument: "Ladda upp ett dokument",
  upload: "Ladda upp",
  uploaded: "Uppladdat.",
  uploadFailed: "Uppladdningen misslyckades. Använd en PDF, PNG eller JPEG på högst 5 MB och fråga igen i chatten.",
  uploadNoAnswer: "Banken svarade inte. Ladda upp filen igen: banken sparar den bara en gång.",
  iUploaded: "Jag har laddat upp {file}.",
  backToChat: "Tillbaka till chatten",
  backToList: "Tillbaka till listan",
  noChats: "Inga chattar än.",
  noQuestion: "(ingen fråga)",
  open: "öppen",
  closed: "stängd",
  readOnly: "skrivskyddad",
  sentTheForm: "Skickade formuläret ”{title}”.",
  cancelled: "Avbrutet",
  confirmed: "Bekräftat",
  confirmedButRefused: "Bekräftat, men banken nekade",
  confirmedNoAnswer: "Bekräftat, men banken svarade inte",
  uploadedFile: "Laddade upp {file} ({status}).",
  aDocument: "ett dokument",
  chartLabel: "Diagram",
  showNumbers: "Visa siffrorna",
};

const fa: Record<TextKey, string> = {
  appTitle: "گفتگوی بانک",
  language: "زبان",
  roleAdmin: "مدیر",
  roleCustomer: "مشتری",
  chat: "گفتگو",
  oldChats: "گفتگوهای قبلی",
  logOut: "خروج",
  loading: "در حال بارگذاری…",
  wrongCredentials: "نام کاربری یا رمز عبور نادرست است.",
  loginFailed: "ورود انجام نشد. دوباره تلاش کنید.",
  noticeSessionExpired: "نشست شما به دلیل بی‌فعالیتی به پایان رسید. لطفاً دوباره وارد شوید.",
  noticeSessionReplaced: "شما از جای دیگری وارد شدید، بنابراین این گفتگو بسته شد. لطفاً دوباره وارد شوید.",
  noticeNewLogin: "یک ورود تازه‌تر این گفتگو را بست.",
  noticeLogout: "از حساب خارج شدید.",
  noticeBankUnavailable: "دسترسی به بانک ممکن نشد. کمی بعد دوباره تلاش کنید.",
  noticeTooManyRuns: "در زمان کوتاهی پیام‌های زیادی فرستادید. یک دقیقه صبر کنید و دوباره تلاش کنید.",
  noticeRunInProgress: "هنوز به پیام دیگری پاسخ داده می‌شود. کمی بعد دوباره تلاش کنید.",
  noticeNoAnswer: "پاسخ زودتر از موعد متوقف شد. بخش کوچک‌تری بخواهید، مثلاً یک صفحه از فهرست.",
  logIn: "ورود",
  loggingIn: "در حال ورود…",
  welcome: "به بانک خوش آمدید. برای شروع گفتگو وارد شوید.",
  username: "نام کاربری",
  password: "رمز عبور",
  demoNote: "این یک نسخهٔ نمایشی است. همهٔ داده‌ها آزمایشی‌اند، هیچ‌چیز واقعی نیست و تغییرها اثر واقعی ندارند.",
  testLogins: "ورودهای آزمایشی",
  admin: "مدیر",
  customer: "مشتری",
  usernameWord: "نام کاربری",
  passwordWord: "رمز عبور",
  anyCustomer: "(هر مشتری: شمارهٔ ملی برای هر دو)",
  greeting: "سلام {name}. شما به عنوان {role} وارد شده‌اید. چه کمکی از من برمی‌آید؟",
  theAdmin: "مدیر",
  aCustomer: "مشتری",
  writeMessage: "پیامی بنویسید",
  message: "پیام",
  send: "ارسال",
  sent: "ارسال شد",
  formGone: "این فرم دیگر قابل ارسال نیست. دوباره در گفتگو بپرسید.",
  iSentTheForm: "فرم را فرستادم.",
  confirm: "تأیید",
  cancel: "لغو",
  sending: "در حال ارسال…",
  done: "انجام شد.",
  cancelledNothingChanged: "لغو شد. هیچ تغییری داده نشد.",
  bankRefused: "بانک این تغییر را انجام نداد",
  changeGone: "این تغییر دیگر قابل تأیید نیست. دوباره در گفتگو بپرسید.",
  bankNoAnswer: "بانک پاسخی نداد، پس معلوم نیست تغییر انجام شده یا نه. دوباره «تأیید» را بزنید؛ بانک آن را فقط یک بار انجام می‌دهد.",
  iConfirmed: "تغییر را تأیید کردم.",
  iCancelled: "تغییر را لغو کردم.",
  uploadDocument: "بارگذاری سند",
  upload: "بارگذاری",
  uploaded: "بارگذاری شد.",
  uploadFailed: "بارگذاری انجام نشد. یک فایل PDF، PNG یا JPEG با حداکثر ۵ مگابایت بفرستید و دوباره در گفتگو بپرسید.",
  uploadNoAnswer: "بانک پاسخی نداد. فایل را دوباره بارگذاری کنید؛ بانک آن را فقط یک بار ذخیره می‌کند.",
  iUploaded: "فایل {file} را بارگذاری کردم.",
  backToChat: "بازگشت به گفتگو",
  backToList: "بازگشت به فهرست",
  noChats: "هنوز گفتگویی نیست.",
  noQuestion: "(بدون پرسش)",
  open: "باز",
  closed: "بسته",
  readOnly: "فقط خواندنی",
  sentTheForm: "فرم «{title}» فرستاده شد.",
  cancelled: "لغو شد",
  confirmed: "تأیید شد",
  confirmedButRefused: "تأیید شد، اما بانک نپذیرفت",
  confirmedNoAnswer: "تأیید شد، اما بانک پاسخی نداد",
  uploadedFile: "فایل {file} بارگذاری شد ({status}).",
  aDocument: "یک سند",
  chartLabel: "نمودار",
  showNumbers: "نمایش اعداد",
};

const texts: Record<Language, Record<TextKey, string>> = { en, sv, fa };

/** A text in a language, with {name}-style placeholders filled in. */
export function text(language: Language, key: TextKey, values: Record<string, string> = {}): string {
  return texts[language][key].replace(/\{(\w+)\}/g, (match, name: string) => values[name] ?? match);
}

const storageKey = "bank-chat-language";

/** The saved choice, else the first browser language we have, else English. */
export function initialLanguage(): Language {
  try {
    const saved = localStorage.getItem(storageKey);
    if (saved === "en" || saved === "sv" || saved === "fa") {
      return saved;
    }
  } catch {
    // Storage can be blocked; the browser language is a fine default.
  }

  for (const preferred of navigator.languages ?? [navigator.language]) {
    const code = preferred.slice(0, 2).toLowerCase();
    if (code === "sv" || code === "fa") {
      return code;
    }
  }

  return "en";
}

/** Saves the choice and sets the page's language and direction (Persian: right to left). */
export function applyLanguage(language: Language) {
  try {
    localStorage.setItem(storageKey, language);
  } catch {
    // Not saved: the page still switches.
  }

  document.documentElement.lang = language;
  document.documentElement.dir = language === "fa" ? "rtl" : "ltr";
}

export const LanguageContext = createContext<Language>("en");

/** The texts of the current language, for components: const t = useText(); t("send"). */
export function useText() {
  const language = useContext(LanguageContext);
  return (key: TextKey, values?: Record<string, string>) => text(language, key, values);
}
