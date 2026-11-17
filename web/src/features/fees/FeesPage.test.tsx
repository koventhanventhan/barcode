import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { ToastProvider } from "@/components/toast";
import i18n from "@/i18n";
import { formatMoney } from "./api";
import { PaymentStatusPage } from "./PaymentStatusPage";

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
    return {};
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
        <I18nextProvider i18n={i18n}>
          <ToastProvider>
            <MemoryRouter initialEntries={["/pay/PH-TEST-001"]}>
              <Routes>
                <Route path="/pay/:orderId" element={<PaymentStatusPage />} />
              </Routes>
            </MemoryRouter>
          </ToastProvider>
        </I18nextProvider>
      </QueryClientProvider>,
    );

    await waitFor(() => {
      expect(screen.getByText("2026 O/L Monthly Tuition (2026-11)")).toBeInTheDocument();
    });

    expect(screen.getByText(/Amaya Silva/)).toBeInTheDocument();
    expect(screen.getByText("INV-000002")).toBeInTheDocument();
    expect(screen.getByText("Complete sandbox payment")).toBeInTheDocument();
  });
});
