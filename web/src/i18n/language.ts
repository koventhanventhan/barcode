export const LOCALES = ["en", "ta", "si"] as const;
export type AppLocale = (typeof LOCALES)[number];

const STORAGE_KEY = "eduflow.lang";

export function readStoredLocale(): AppLocale {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === "ta" || stored === "si" || stored === "en") return stored;
  } catch {
    /* private mode */
  }
  return "en";
}

export function applyDocumentLanguage(locale: AppLocale): void {
  document.documentElement.lang = locale;
  try {
    localStorage.setItem(STORAGE_KEY, locale);
  } catch {
    /* private mode */
  }
}
