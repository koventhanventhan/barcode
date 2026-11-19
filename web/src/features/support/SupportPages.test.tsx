import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { StaticFeatureProvider } from "@/components/feature-gate";
import { ToastProvider } from "@/components/toast";
import { SupportInboxPage } from "@/features/super-admin/SupportInboxPage";
import { SupportTicketsPage } from "./SupportTicketsPage";

globalThis.ResizeObserver = class {
  observe() {}
  unobserve() {}
  disconnect() {}
};

vi.mock("@/features/auth/AuthProvider", () => ({
  useAuth: () => ({ hasPermission: () => true, isReadOnly: false }),
}));

const apiMock = vi.fn(async (path: string, init?: RequestInit) => {
  if (path.startsWith("/api/support/tickets?")) {
    return {
      items: [
        {
          id: "tkt-1",
          ticketNumber: "TKT-000001",
          subject: "Need help with backup",
          category: "BackupRestore",
          status: "Open",
          isPriority: true,
          createdByName: "Admin",
          createdByEmail: "admin@demo.admino.lk",
          messageCount: 1,
          lastMessagePreview: "Please verify archive.",
          lastMessageAt: "2026-11-19T05:00:00Z",
          resolvedAt: null,
          closedAt: null,
          createdAt: "2026-11-19T05:00:00Z",
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    };
  }
  if (path === "/api/support/tickets/tkt-1") {
    return {
      id: "tkt-1",
      ticketNumber: "TKT-000001",
      subject: "Need help with backup",
      category: "BackupRestore",
      status: "Open",
      isPriority: true,
      createdByName: "Admin",
      createdByEmail: "admin@demo.admino.lk",
      lastMessageAt: "2026-11-19T05:00:00Z",
      resolvedAt: null,
      closedAt: null,
      createdAt: "2026-11-19T05:00:00Z",
      messages: [
        {
          id: "m-1",
          senderUserId: "u-1",
          senderName: "Admin",
          senderRole: "InstituteAdmin",
          isFromSuperAdmin: false,
          body: "Please verify archive.",
          createdAt: "2026-11-19T05:00:00Z",
        },
      ],
    };
  }
  if (path.startsWith("/api/super-admin/support/tickets?")) {
    return {
      openCount: 1,
      priorityOpenCount: 1,
      waitingCount: 0,
      resolvedCount: 0,
      items: [
        {
          id: "tkt-1",
          tenantId: "ten-1",
          tenantName: "Royal Vidyalaya",
          tenantSubdomain: "demo",
          planName: "Pro",
          ticketNumber: "TKT-000001",
          subject: "Need help with backup",
          category: "BackupRestore",
          status: "Open",
          isPriority: true,
          createdByName: "Admin",
          createdByEmail: "admin@demo.admino.lk",
          messageCount: 1,
          lastMessagePreview: "Please verify archive.",
          lastMessageAt: "2026-11-19T05:00:00Z",
          resolvedAt: null,
          closedAt: null,
          createdAt: "2026-11-19T05:00:00Z",
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    };
  }
  if (path === "/api/super-admin/support/tickets/tkt-1") {
    return {
      id: "tkt-1",
      tenantId: "ten-1",
      tenantName: "Royal Vidyalaya",
      tenantSubdomain: "demo",
      planName: "Pro",
      ticketNumber: "TKT-000001",
      subject: "Need help with backup",
      category: "BackupRestore",
      status: "Open",
      isPriority: true,
      createdByName: "Admin",
      createdByEmail: "admin@demo.admino.lk",
      lastMessageAt: "2026-11-19T05:00:00Z",
      resolvedAt: null,
      closedAt: null,
      createdAt: "2026-11-19T05:00:00Z",
      messages: [
        {
          id: "m-1",
          senderUserId: "u-1",
          senderName: "Admin",
          senderRole: "InstituteAdmin",
          isFromSuperAdmin: false,
          body: "Please verify archive.",
          createdAt: "2026-11-19T05:00:00Z",
        },
      ],
    };
  }
  if (path === "/api/super-admin/support/tickets/tkt-1/reply" && init?.method === "POST") {
    return { id: "tkt-1", status: "WaitingOnInstitute" };
  }
  return {};
});

vi.mock("@/lib/api", () => ({
  api: (...args: unknown[]) => apiMock(...(args as [string, RequestInit?])),
  ApiError: class extends Error {},
}));

describe("Support tickets and Super Admin inbox", () => {
  it("renders tenant support tickets with Priority support badge and conversation thread", async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <StaticFeatureProvider
          features={[
            {
              featureKey: "PrioritySupport",
              isInPlan: true,
              isOverridden: false,
              isTenantEnabled: true,
              isEffectiveEnabled: true,
              updatedAt: null,
            },
          ]}
        >
          <ToastProvider>
            <MemoryRouter>
              <SupportTicketsPage />
            </MemoryRouter>
          </ToastProvider>
        </StaticFeatureProvider>
      </QueryClientProvider>,
    );

    expect(await screen.findByRole("heading", { name: "Need help with backup" })).toBeInTheDocument();
    expect(screen.getAllByText("Priority support").length).toBeGreaterThan(0);
    expect((await screen.findAllByText("Please verify archive.")).length).toBeGreaterThan(0);
  });

  it("renders Super Admin support inbox and sends a reply", async () => {
    const user = userEvent.setup();
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <ToastProvider>
          <MemoryRouter>
            <SupportInboxPage />
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>,
    );

    expect(await screen.findByRole("heading", { name: "Support inbox" })).toBeInTheDocument();
    expect(await screen.findByText("Please verify archive.")).toBeInTheDocument();

    await user.type(screen.getByLabelText("Send reply"), "Dry run looks healthy.");
    await user.click(screen.getByRole("button", { name: "Send reply" }));

    await waitFor(() => {
      expect(apiMock).toHaveBeenCalledWith(
        "/api/super-admin/support/tickets/tkt-1/reply",
        expect.objectContaining({ method: "POST" }),
      );
    });
  });
});
