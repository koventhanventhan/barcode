import manrope700Url from "@fontsource/manrope/files/manrope-latin-700-normal.woff2?url";
import inter400Url from "@fontsource/inter/files/inter-latin-400-normal.woff2?url";
import "@fontsource/manrope/600.css";
import "@fontsource/manrope/700.css";
import "@fontsource/manrope/800.css";
import "@fontsource/inter/400.css";
import "@fontsource/inter/500.css";
import "@fontsource/inter/600.css";
import "@fontsource/noto-sans-tamil/400.css";
import "@fontsource/noto-sans-tamil/600.css";
import "@fontsource/noto-sans-sinhala/400.css";
import "@fontsource/noto-sans-sinhala/600.css";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App from "@/app/App.tsx";
import "@/styles/globals.css";

function preloadFont(href: string) {
  if (typeof document === "undefined") return;
  if (document.querySelector(`link[rel="preload"][href="${href}"]`)) return;
  const link = document.createElement("link");
  link.rel = "preload";
  link.as = "font";
  link.type = "font/woff2";
  link.crossOrigin = "anonymous";
  link.href = href;
  document.head.appendChild(link);
}

preloadFont(manrope700Url);
preloadFont(inter400Url);

const root = document.getElementById("root");
if (!root) {
  throw new Error("Root element #root is missing.");
}

createRoot(root).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
