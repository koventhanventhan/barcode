import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { QrCodeSvg } from "@/components/qr-code";
import { ToastProvider } from "@/components/toast";
import { StaffRegisterPage } from "./StaffRegisterPage";
import { StaffRegistrationsPanel } from "./StaffRegistrationsPanel";

globalThis.ResizeObserver = class {
  observe() {}
  unobserve() {}
  disconnect() {}
};

const apiMock = vi.fn(async (path: string, init?: RequestInit) => {
  if (path === "/api/public/staff-registration/tok-valid") {
    return {
      instituteName: "Royal Vidyalaya",
      tagline: "Colombo Campus",
      logoUrl: null,
      primaryColor: "#0F766E",
      accentColor: "#14213D",
      defaultBranchId: null,
      branches: [{ id: "b-1", name: "Main Campus" }],
    };
  }
  if (path === "/api/public/staff-registration/tok-valid" && init?.method === "POST") {
    return {
      requestId: "req-1",
      status: "Pending",
      message: "Registration submitted.",
    };
  }
  if (path === "/api/staff/registration-link") {
    return {
      hasLink: true,
      isEnabled: true,
      token: "tok-valid",
      registrationUrl: "http://localhost:5173/staff-register?token=tok-valid",
      qr: { size: 3, modules: "101010101" },
      defaultBranchId: null,
      updatedAt: "2026-11-19T05:00:00Z",
    };
  }
  if (path.startsWith("/api/staff/registrations?")) {
    return {
      items: [
        {
          id: "req-1",
          fullName: "Kavindi Perera",
          email: "kavindi@staff.lk",
          phone: "0771234567",
          requestedRoleType: "Teacher",
          branchId: null,
          branchName: null,
          notes: "Mathematics",
          status: "Pending",
          reviewedByUserId: null,
          reviewedAt: null,
          reviewNotes: null,
          createdUserId: null,
          createdAt: "2026-11-19T05:10:00Z",
        },
      ],
      totalCount: 1,
      pendingCount: 1,
      page: 1,
      pageSize: 20,
    };
  }
  if (path === "/api/staff/registrations/req-1/approve" && init?.method === "POST") {
    return {
      id: "req-1",
      status: "Approved",
    };
  }
  return {};
});

vi.mock("@/lib/api", () => ({
  api: (...args: unknown[]) => apiMock(...(args as [string, RequestInit?])),
  ApiError: class extends Error {},
}));

describe("Staff self-registration and QR", () => {
  it("renders a crisp QR SVG from module matrix", () => {
    render(<QrCodeSvg qr={{ size: 2, modules: "1001" }} label="Registration QR" />);
    const svg = screen.getByRole("img", { name: "Registration QR" });
    expect(svg.querySelectorAll("rect")).toHaveLength(3);
  });

  it("renders public staff registration form and submits a pending request", async () => {
    const user = userEvent.setup();
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <ToastProvider>
          <MemoryRouter initialEntries={["/staff-register?token=tok-valid"]}>
            <Routes>
              <Route path="/staff-register" element={<StaffRegisterPage />} />
            </Routes>
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>,
    );

    expect(await screen.findByText("Royal Vidyalaya")).toBeInTheDocument();
    await user.type(screen.getByLabelText("Name"), "Kavindi Perera");
    await user.type(screen.getByLabelText("Email"), "kavindi@staff.lk");
    await user.type(screen.getByLabelText("Password"), "StaffPass@123!");
    await user.type(screen.getByLabelText("Confirm password"), "StaffPass@123!");
    await user.click(screen.getByRole("button", { name: "Submit registration" }));

    expect(await screen.findByText("Registration submitted")).toBeInTheDocument();
  });

  it("renders admin registration link, QR, and approves pending staff", async () => {
    const user = userEvent.setup();
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <ToastProvider>
          <MemoryRouter>
            <StaffRegistrationsPanel
              templates={[
                { key: "front-desk", name: "Front desk", roleType: "Staff", permissions: [] },
                { key: "class-teacher", name: "Class teacher", roleType: "Teacher", permissions: [] },
              ]}
              branches={[]}
              canManage
            />
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>,
    );

    expect(await screen.findByRole("img", { name: "Staff self-registration QR code" })).toBeInTheDocument();
    expect(await screen.findByText("Kavindi Perera")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Approve" }));
    expect(await screen.findByText("Approve & create account")).toBeInTheDocument();
    const approveButtons = screen.getAllByRole("button", { name: "Approve" });
    await user.click(approveButtons[approveButtons.length - 1]);

    await waitFor(() => {
      expect(apiMock).toHaveBeenCalledWith(
        "/api/staff/registrations/req-1/approve",
        expect.objectContaining({ method: "POST" }),
      );
    });
  });
});
