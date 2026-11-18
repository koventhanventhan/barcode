import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { StaticFeatureProvider } from "@/components/feature-gate";
import { ToastProvider } from "@/components/toast";
import { formatMoney } from "./api";
import { FeesPage } from "./FeesPage";
import { PaymentStatusPage } from "./PaymentStatusPage";

globalThis.ResizeObserver = class {
  observe() {}
  unobserve() {}
  disconnect() {}
};

vi.mock("@/features/auth/AuthProvider", () => ({
  useAuth: () => ({ hasPermission: () => true, isReadOnly: false }),
}));

vi.mock("@/components/layout/BranchSwitcher", () => ({
  BranchSwitcher: () => null,
}));

vi.mock("@/features/branding/BrandingProvider", () => ({
  useBranding: () => ({
    branding: { instituteDisplayName: "Royal Vidyalaya" },
    resolvedMode: "light",
    setTheme: vi.fn(),
    refetch: vi.fn(),
  }),
}));

vi.mock("@/lib/api", () => ({
  api: vi.fn(async (path: string) => {
    if (path.includes("/api/fees/payhere/orders/PH-TEST-001")) {
      return {
        sessionId: "11111111-1111-1111-1111-111111111111",
        orderId: "PH-TEST-001",
        invoiceId: "22222222-2222-2222-2222-222222222222",
        invoiceNumber: "INV-000002",
        invoiceTitle: "2026 O/L Monthly Tuition (2026-11)",
        studentId: "33333333-3333-3333-3333-333333333333",
        studentName: "Amaya Silva",
        studentCode: "S000002",
        amount: 3150,
        invoiceNetAmount: 3150,
        invoiceBalanceAmount: 3150,
        currency: "LKR",
        isSandbox: true,
        checkoutUrl: "https://sandbox.payhere.lk/pay/checkout",
        status: "Initiated",
        payHerePaymentId: null,
        feePaymentId: null,
        receiptNumber: null,
        formFields: {
          merchant_id: "1211149",
          order_id: "PH-TEST-001",
          amount: "3150.00",
          currency: "LKR",
          hash: "ABCDEF1234567890ABCDEF1234567890",
        },
        createdAt: "2026-11-17T10:00:00Z",
        completedAt: null,
      };
    }
    if (path.startsWith("/api/fees/dashboard")) {
      return {
        expectedTotal: 10000,
        collectedTotal: 4000,
        outstandingTotal: 6000,
        overdueTotal: 2000,
        collectionRatePercent: 40,
        paidInvoiceCount: 1,
        partiallyPaidInvoiceCount: 1,
        pendingInvoiceCount: 1,
        overdueInvoiceCount: 1,
        overdueAging: [],
        byBatch: [],
        topDefaulters: [],
        recentPayments: [],
      };
    }
    if (path.startsWith("/api/fees/invoices")) {
      return { items: [], totalCount: 0, page: 1, pageSize: 100 };
    }
    if (path.startsWith("/api/fees/bank-slips")) {
      return [
        {
          id: "slip-1",
          invoiceId: "inv-1",
          invoiceNumber: "INV-000010",
          invoiceTitle: "Monthly Tuition",
          studentId: "stu-1",
          studentCode: "S000010",
          studentName: "Kasun Perera",
          submittedByUserId: "u-1",
          submittedByName: "Parent Perera",
          submittedByRole: "Parent",
          amount: 4000,
          invoiceBalanceAmount: 4000,
          currency: "LKR",
          referenceNumber: "BNK-901",
          transferDate: "2026-11-18",
          slipFileName: "slip.png",
          status: "Pending",
          notes: null,
          reviewedByUserId: null,
          reviewedByName: null,
          reviewedAt: null,
          rejectionReason: null,
          feePaymentId: null,
          receiptNumber: null,
          createdAt: "2026-11-18T10:00:00Z",
        },
      ];
    }
    if (path.startsWith("/api/fees/credits")) {
      return [
        {
          studentId: "stu-1",
          studentCode: "S000010",
          studentName: "Kasun Perera",
          guardianName: "Parent Perera",
          guardianPhone: "+94771112233",
          balance: 1500,
          currency: "LKR",
          lastActivityAt: "2026-11-18T09:00:00Z",
          transactions: [],
        },
      ];
    }
    if (path === "/api/fees/sibling-discount") {
      return {
        isEnabled: true,
        discountType: "Percentage",
        secondChildValue: 10,
        thirdPlusChildValue: 20,
        applyToMonthly: true,
        applyToOneTime: false,
        applyToAdmission: false,
        stackWithConcession: false,
      };
    }
    if (path.startsWith("/api/fees/reports/cashier-closing")) {
      return {
        date: "2026-11-18",
        currency: "LKR",
        totalTransactions: 2,
        invoicePaymentsTotal: 4000,
        advanceDepositsTotal: 1500,
        refundedTotal: 0,
        grandTotalCollected: 5500,
        byMethod: [
          { method: "Cash", transactionCount: 1, invoicePaymentsTotal: 4000, advanceDepositsTotal: 0, refundedTotal: 0, netCollectedTotal: 4000 },
          { method: "BankTransfer", transactionCount: 1, invoicePaymentsTotal: 0, advanceDepositsTotal: 1500, refundedTotal: 0, netCollectedTotal: 1500 },
        ],
        byUser: [
          { userId: "u-admin", cashierName: "Nimali Silva", cashierEmail: "nimali@demo.lk", transactionCount: 2, cashTotal: 4000, bankTransferTotal: 1500, payHereTotal: 0, advanceDepositsTotal: 1500, refundedTotal: 0, netCollectedTotal: 5500 },
        ],
        items: [],
      };
    }
    if (path.startsWith("/api/students")) {
      return { items: [] };
    }
    return [];
  }),
  ApiError: class extends Error {},
}));

describe("Fees and PayHere UI", () => {
  it("formats Sri Lankan Rupee amounts with two decimals", () => {
    expect(formatMoney(3500, "LKR")).toBe("LKR 3,500.00");
    expect(formatMoney(1500.5, "LKR")).toBe("LKR 1,500.50");
  });

  it("renders PaymentStatusPage with sandbox payment controls and order details", async () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    render(
      <QueryClientProvider client={queryClient}>
                  <ToastProvider>
            <MemoryRouter initialEntries={["/pay/PH-TEST-001"]}>
              <Routes>
                <Route path="/pay/:orderId" element={<PaymentStatusPage />} />
              </Routes>
            </MemoryRouter>
          </ToastProvider>
      </QueryClientProvider>,
    );

    await waitFor(() => {
      expect(screen.getByText("2026 O/L Monthly Tuition (2026-11)")).toBeInTheDocument();
    });

    expect(screen.getByText(/Amaya Silva/)).toBeInTheDocument();
    expect(screen.getByText("INV-000002")).toBeInTheDocument();
    expect(screen.getByText("Complete sandbox payment")).toBeInTheDocument();
  });

  it("renders bank approvals, advance credits, and daily cashier closing report tabs", async () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    const user = userEvent.setup();

    render(
      <QueryClientProvider client={queryClient}>
                  <ToastProvider>
            <StaticFeatureProvider
              features={[
                { featureKey: "DataExport", isInPlan: true, isOverridden: false, isTenantEnabled: true, isEffectiveEnabled: true, updatedAt: null },
                { featureKey: "OnlinePayments", isInPlan: true, isOverridden: false, isTenantEnabled: true, isEffectiveEnabled: true, updatedAt: null },
              ]}
            >
              <MemoryRouter>
                <FeesPage />
              </MemoryRouter>
            </StaticFeatureProvider>
          </ToastProvider>
      </QueryClientProvider>,
    );

    const bankTab = await screen.findByRole("tab", { name: /Bank approvals/i });
    await user.click(bankTab);
    expect(await screen.findByText("INV-000010")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reject" })).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: /Advance credits/i }));
    expect(await screen.findByText("Kasun Perera")).toBeInTheDocument();
    expect(screen.getByText("LKR 1,500.00")).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: /Cashier closing/i }));
    expect(await screen.findByText("Nimali Silva")).toBeInTheDocument();
    expect(screen.getAllByText("LKR 5,500.00").length).toBeGreaterThan(0);
  });
});
