import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { ToastProvider } from "@/components/toast";
import { SmsSettingsPage } from "./SmsSettingsPage";

globalThis.ResizeObserver = class {
  observe() {}
  unobserve() {}
  disconnect() {}
};

const settingsResponse = {
  provider: "NotifyLk",
  userId: "user-42",
  hasApiKey: true,
  apiKeySource: "Database",
  senderId: "Admino",
  promotionalSenderId: null,
  baseUrl: "https://app.notify.lk/api/v1",
  testMode: true,
  testAllowList: ["94771112233"],
  lowBalanceThreshold: 500,
  lastLowBalanceAlertSentAt: null,
  recentErrors: [
    {
      id: "err-1",
      tenantId: null,
      provider: "Notify.lk",
      operation: "Send",
      recipient: "9477***888",
      errorCode: "INSUFFICIENT_BALANCE",
      errorMessage: "Insufficient balance",
      occurredAt: "2026-11-18T10:00:00Z",
    },
  ],
};

const balanceResponse = {
  succeeded: true,
  provider: "Notify.lk",
  isActive: true,
  balance: 350.0,
  currency: "LKR",
  isBelowThreshold: true,
  lowBalanceThreshold: 500,
  checkedAt: "2026-11-18T10:05:00Z",
  fromCache: true,
  errorCode: null,
  errorMessage: null,
};

vi.mock("@/lib/api", () => ({
  api: vi.fn(async (path: string) => {
    if (path === "/api/super-admin/settings/sms") return settingsResponse;
    if (path.startsWith("/api/super-admin/settings/sms/balance")) return balanceResponse;
    return {};
  }),
  ApiError: class extends Error {},
}));

describe("Super Admin SMS settings page", () => {
  it("renders balance, write-only API key field, and recent gateway errors", async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <ToastProvider>
          <MemoryRouter>
            <SmsSettingsPage />
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>,
    );

    expect(await screen.findByText("350.00 LKR")).toBeInTheDocument();
    expect(screen.getByText(/below the 500.00 LKR alert threshold/i)).toBeInTheDocument();
    expect(screen.getByText("Saved (encrypted in database)")).toBeInTheDocument();

    const apiKeyInput = screen.getByLabelText("API key (write-only)") as HTMLInputElement;
    expect(apiKeyInput.type).toBe("password");
    expect(apiKeyInput.value).toBe("");

    expect(screen.getByText("INSUFFICIENT_BALANCE")).toBeInTheDocument();
  });
});
