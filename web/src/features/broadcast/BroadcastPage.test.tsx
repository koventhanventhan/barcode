import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { StaticFeatureProvider } from "@/components/feature-gate";
import { ToastProvider } from "@/components/toast";
import i18n from "@/i18n";
import { BroadcastPage } from "./BroadcastPage";

const preview = {
  recipientCount: 2,
  skippedCount: 0,
  estimatedSegments: 4,
  sampleSegments: 2,
  sampleBody: "Hello from the institute",
  encoding: "GSM-7",
  isUnicode: false,
  characterLimit: 153,
  usedSegments: 10,
  monthlyQuota: 300,
  remainingSegments: 290,
  wouldExceedQuota: false,
  overQuotaPolicy: "AllowAndRecordExtra",
  quietHoursActive: false,
  quietHoursResumeAt: null,
};

vi.mock("@/features/auth/AuthProvider", () => ({
  useAuth: () => ({ hasPermission: () => true }),
}));

vi.mock("@/lib/api", () => ({
  api: vi.fn(async (path: string) => {
    if (path === "/api/broadcasts/preview") return preview;
    if (path.startsWith("/api/broadcasts")) return { items: [] };
    if (path === "/api/education-levels") return [];
    if (path === "/api/batches") return [];
    return [];
  }),
  ApiError: class extends Error {},
}));

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <I18nextProvider i18n={i18n}>
        <ToastProvider>
          <StaticFeatureProvider
            features={[
              { featureKey: "BroadcastMessages", isInPlan: true, isOverridden: false, isTenantEnabled: true, isEffectiveEnabled: true, updatedAt: null },
              { featureKey: "BatchGroups", isInPlan: true, isOverridden: false, isTenantEnabled: true, isEffectiveEnabled: true, updatedAt: null },
              { featureKey: "EmailNotifications", isInPlan: true, isOverridden: false, isTenantEnabled: true, isEffectiveEnabled: true, updatedAt: null },
              { featureKey: "WhatsAppMessaging", isInPlan: false, isOverridden: false, isTenantEnabled: false, isEffectiveEnabled: false, updatedAt: null },
            ]}
          >
            <MemoryRouter>
              <BroadcastPage />
            </MemoryRouter>
          </StaticFeatureProvider>
        </ToastProvider>
      </I18nextProvider>
    </QueryClientProvider>,
  );
}

describe("Broadcast page", () => {
  beforeEach(async () => {
    await i18n.changeLanguage("en");
  });

  it("shows the live recipient count and segment estimate, then asks before sending", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(await screen.findByRole("textbox", { name: "Message" }), "Classes resume Monday");
    expect(await screen.findByText("2 recipients")).toBeInTheDocument();
    expect(screen.getByText("4 SMS segments")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Review and send" }));
    expect(await screen.findByRole("dialog")).toHaveTextContent("2 people");
    expect(screen.getByRole("dialog")).toHaveTextContent("4 SMS segments");
  });
});
