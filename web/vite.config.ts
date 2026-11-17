import path from "node:path";
import { fileURLToPath } from "node:url";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { VitePWA } from "vite-plugin-pwa";
import { defineConfig } from "vitest/config";

const root = path.dirname(fileURLToPath(import.meta.url));
const apiTarget = process.env.VITE_PROXY_TARGET ?? "http://localhost:8080";

export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      disable: process.env.VITEST === "true",
      registerType: "autoUpdate",
      includeAssets: ["favicon.svg", "icons/icon-192.png", "icons/icon-512.png"],
      manifest: {
        name: "EduFlow Parent",
        short_name: "EduFlow",
        description: "Attendance, fees, results, and messages for parents.",
        theme_color: "#1f4e46",
        background_color: "#f4f6f5",
        display: "standalone",
        start_url: "/parent",
        scope: "/",
        icons: [
          { src: "/icons/icon-192.png", sizes: "192x192", type: "image/png", purpose: "any" },
          { src: "/icons/icon-512.png", sizes: "512x512", type: "image/png", purpose: "any" },
          { src: "/icons/icon-512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
        ],
      },
      workbox: {
        navigateFallback: "/index.html",
        globPatterns: ["**/*.{js,css,html,svg,png,ico,woff2}"],
      },
    }),
  ],
  resolve: {
    alias: {
      "@": path.resolve(root, "./src"),
    },
  },
  build: {
    rollupOptions: {
      output: {
        manualChunks: {
          "react-vendor": ["react", "react-dom", "react-router-dom", "react-helmet-async"],
          charts: ["recharts"],
          scanner: ["@zxing/browser"],
          realtime: ["@microsoft/signalr"],
        },
      },
    },
  },
  server: {
    port: 5173,
    proxy: {
      "/api": apiTarget,
      "/health": apiTarget,
      "/files": apiTarget,
      "/hubs": { target: apiTarget, ws: true },
    },
  },
  preview: {
    port: 4173,
    proxy: {
      "/api": apiTarget,
      "/health": apiTarget,
      "/files": apiTarget,
      "/hubs": { target: apiTarget, ws: true },
    },
  },
  test: {
    environment: "jsdom",
    include: ["src/**/*.test.ts", "src/**/*.test.tsx"],
    setupFiles: ["./src/test/setup.ts"],
    testTimeout: 20000,
    coverage: {
      provider: "v8",
      include: [
        "src/config/**/*.ts",
        "src/lib/**/*.ts",
        "src/components/**/*.{ts,tsx}",
        "src/features/auth/guards.tsx",
        "src/features/auth/password.ts",
        "src/features/exams/marksGrid.ts",
        "src/features/help/articles.ts",
        "src/features/marketing/price.ts",
        "src/features/parent/model.ts",
      ],
      exclude: ["src/**/*.test.{ts,tsx}", "src/lib/types.ts"],
      thresholds: {
        lines: 60,
        statements: 60,
      },
    },
  },
});
