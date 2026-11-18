import { QueryClient as QC, QueryClientProvider as QCP } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HelmetProvider } from "react-helmet-async";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StaticFeatureProvider } from "@/components/feature-gate";
import { AppShell } from "@/components/layout/AppShell";
import { ToastProvider } from "@/components/toast";
import { AuthContext, type AuthContextValue } from "@/features/auth/AuthProvider";
import { BrandLogo } from "@/features/branding/BrandLogo";
import { BrandingProvider } from "@/features/branding/BrandingProvider";
import { MarketingLayout } from "@/features/marketing/MarketingLayout";
import { extractInstituteCodeFromHost, monogramFor } from "@/lib/subdomain";
import { AcceptInvitePage, ForgotPasswordPage, LoginPage } from "./AuthPages";

const loginMock = vi.fn();

const guestAuth: AuthContextValue = {
  user: null,
  isReady: true,
  isReadOnly: false,
  isImpersonating: false,
  login: async (...args) => {
    await loginMock(...args);
    return "SuperAdmin";
  },
  adoptSession: async () => "SuperAdmin",
  logout: async () => undefined,
  hasPermission: () => false,
  impersonate: async () => undefined,
  exitImpersonation: () => undefined,
};

function renderWithBranding(route: string, authValue: AuthContextValue = guestAuth) {
  const client = new QC({ defaultOptions: { queries: { retry: false } } });
  return render(
    <HelmetProvider>
      <QCP client={client}>
        <ToastProvider>
          <AuthContext.Provider value={authValue}>
            <MemoryRouter initialEntries={[route]}>
              <BrandingProvider>
                <StaticFeatureProvider features={[]}>
                  <Routes>
                    <Route path="/login" element={<LoginPage />} />
                    <Route path="/forgot-password" element={<ForgotPasswordPage />} />
                    <Route path="/accept-invite" element={<AcceptInvitePage />} />
                    <Route path="/admin/tenants" element={<p>Admin home</p>} />
                    <Route path="/marketing" element={<MarketingLayout><p>Marketing</p></MarketingLayout>} />
                    <Route path="/app" element={<AppShell />} />
                  </Routes>
                </StaticFeatureProvider>
              </BrandingProvider>
            </MemoryRouter>
          </AuthContext.Provider>
        </ToastProvider>
      </QCP>
    </HelmetProvider>,
  );
}

describe("LoginPage and brand-aware auth", () => {
  beforeEach(() => {
    localStorage.clear();
    loginMock.mockReset();
    loginMock.mockResolvedValue("SuperAdmin");
    window.scrollTo = vi.fn();
    let link = document.querySelector<HTMLLinkElement>("link[rel='icon']");
    if (!link) {
      link = document.createElement("link");
      link.rel = "icon";
      link.href = "/favicon.svg";
      document.head.appendChild(link);
    }
    window.matchMedia = vi.fn().mockImplementation((query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }));

    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input);
        if (url.includes("/api/public/branding?code=alpha")) {
          return new Response(
            JSON.stringify({
              displayName: "Alpha Academy",
              tagline: "Quiet excellence",
              logoUrl: "/files/branding/alpha-logo.png",
              faviconUrl: "/files/branding/alpha-fav.png",
              primaryColor: "#112233",
              accentColor: "#C45C26",
            }),
            { status: 200 },
          );
        }
        if (url.includes("/api/public/branding")) {
          return new Response(
            JSON.stringify({
              displayName: "Admino",
              tagline: "Platform operations",
              logoUrl: null,
              faviconUrl: null,
              primaryColor: "#1F4E46",
              accentColor: "#C45C26",
            }),
            { status: 200 },
          );
        }
        if (url.includes("/api/branding/current")) {
          return new Response(
            JSON.stringify({
              primaryColor: "#112233",
              accentColor: "#C45C26",
              logoUrl: "/files/branding/alpha-logo.png",
              faviconUrl: "/files/branding/alpha-fav.png",
              loginBackgroundUrl: null,
              instituteDisplayName: "Alpha Academy",
              tagline: "Quiet excellence",
              fontPreference: "Inter",
              themeMode: "light",
              removeProductBranding: false,
              canRemoveProductBranding: false,
              subdomain: "alpha",
              isPlatformDefault: false,
            }),
            { status: 200 },
          );
        }
        return new Response(JSON.stringify([]), { status: 200 });
      }),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("lets a platform operator submit with a blank institute code", async () => {
    const user = userEvent.setup();
    renderWithBranding("/login");

    await user.type(screen.getByLabelText("Email"), "superadmin@admino.lk");
    await user.type(screen.getByLabelText("Password"), "SuperAdmin@123!");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    expect(screen.queryByText("Required")).not.toBeInTheDocument();
    expect(loginMock).toHaveBeenCalledWith("superadmin@admino.lk", "SuperAdmin@123!", "");
    expect(await screen.findByText("Admin home")).toBeInTheDocument();
  });

  it("loads platform brand first, swaps brand after 400ms debounce, and returns to platform when cleared", async () => {
    const user = userEvent.setup();
    renderWithBranding("/login");

    expect(await screen.findAllByText("Admino")).not.toHaveLength(0);
    expect(screen.getAllByLabelText("Admino monogram").length).toBeGreaterThan(0);

    const instituteInput = screen.getByLabelText(/Institute code/i);
    await user.type(instituteInput, "alpha");

    await waitFor(
      () => {
        expect(screen.getAllByText("Alpha Academy").length).toBeGreaterThan(0);
      },
      { timeout: 1500 },
    );

    expect(document.title).toBe("Alpha Academy");
    expect(document.querySelector<HTMLLinkElement>("link[rel='icon']")?.href).toContain("/files/branding/alpha-fav.png");
    expect(screen.getAllByAltText("Alpha Academy logo").length).toBeGreaterThan(0);

    await user.clear(instituteInput);
    await waitFor(
      () => {
        expect(document.title).toBe("Admino");
        expect(screen.queryByText("Alpha Academy")).not.toBeInTheDocument();
      },
      { timeout: 1500 },
    );
    expect(screen.getAllByLabelText("Admino monogram").length).toBeGreaterThan(0);
  });

  it("falls back to monogram when logo fails to load or is absent", () => {
    expect(monogramFor("Royal Vidyalaya")).toBe("RO");
    expect(monogramFor("Admino")).toBe("AD");

    render(<BrandLogo logoUrl="/broken.png" displayName="Royal Vidyalaya" size="lg" />);
    const img = screen.getByAltText("Royal Vidyalaya logo");
    fireEvent.error(img);
    expect(screen.getByLabelText("Royal Vidyalaya monogram")).toHaveTextContent("RO");
  });

  it("renders brand loading on forgot-password and accept-invite without any language switcher", async () => {
    const user = userEvent.setup();
    renderWithBranding("/forgot-password");
    expect(screen.queryByLabelText(/language/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("option", { name: "Tamil" })).not.toBeInTheDocument();

    await user.type(screen.getByLabelText(/Institute code/i), "alpha");
    await waitFor(
      () => {
        expect(screen.getAllByText("Alpha Academy").length).toBeGreaterThan(0);
      },
      { timeout: 1500 },
    );

    renderWithBranding("/accept-invite?token=abc");
    expect(screen.queryByLabelText(/language/i)).not.toBeInTheDocument();
  });

  it("has no language switcher in MarketingLayout or AppShell and displays institute logo after login", async () => {
    renderWithBranding("/marketing");
    expect(screen.queryByLabelText(/language/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("option", { name: "Tamil" })).not.toBeInTheDocument();
    expect(screen.queryByRole("option", { name: "Sinhala" })).not.toBeInTheDocument();

    renderWithBranding("/app", {
      ...guestAuth,
      user: {
        id: "u1",
        tenantId: "t1",
        email: "admin@alpha.lk",
        fullName: "Alpha Admin",
        role: "InstituteAdmin",
        permissions: ["settings.manage"],
        mustChangePassword: false,
      },
    });
    expect(screen.queryByLabelText(/language/i)).not.toBeInTheDocument();
    expect(await screen.findAllByAltText("Alpha Academy logo")).not.toHaveLength(0);
    expect(screen.getAllByText("Alpha Academy").length).toBeGreaterThan(0);
  });

  it("extracts institute code from tenant subdomains only", () => {
    expect(extractInstituteCodeFromHost("alpha.admino.lk")).toBe("alpha");
    expect(extractInstituteCodeFromHost("demo.localhost")).toBe("demo");
    expect(extractInstituteCodeFromHost("localhost")).toBe("");
    expect(extractInstituteCodeFromHost("admino.lk")).toBe("");
    expect(extractInstituteCodeFromHost("www.admino.lk")).toBe("");
  });
});
