import i18n from "i18next";
import { initReactI18next } from "react-i18next";
import en from "./en.json";
import { applyDocumentLanguage, readStoredLocale } from "./language";
import si from "./si.json";
import ta from "./ta.json";

const locale = readStoredLocale();

void i18n.use(initReactI18next).init({
  resources: {
    en: { translation: en },
    ta: { translation: ta },
    si: { translation: si },
  },
  lng: locale,
  fallbackLng: "en",
  interpolation: { escapeValue: false },
  initImmediate: false,
});

applyDocumentLanguage(locale);

i18n.on("languageChanged", (language) => {
  if (language === "en" || language === "ta" || language === "si") {
    applyDocumentLanguage(language);
  }
});

export default i18n;
