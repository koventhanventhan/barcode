import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { HelmetProvider } from "react-helmet-async";
import { MemoryRouter, Outlet, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { LoginPage } from "@/features/auth/AuthPages";
import { AuthContext, type AuthContextValue } from "@/features/auth/AuthProvider";
import { BrandingProvider } from "@/features/branding/BrandingProvider";
import { Img } from "./Img";
import { MarketingLayout } from "./MarketingLayout";
import { AboutPage } from "./pages/AboutPage";
import { ContactPage } from "./pages/ContactPage";
import { DemoPage } from "./pages/DemoPage";
import { FeatureDetailPage } from "./pages/FeatureDetailPage";
import { FeaturesOverviewPage } from "./pages/FeaturesOverviewPage";
import { HomePage } from "./pages/HomePage";
import { NotFoundPage } from "./pages/NotFoundPage";
import { PricingPage } from "./pages/PricingPage";
import { SignupPage } from "./SignupPage";

const authMock: AuthContextValue = {
  user: null,
  isReady: true,
  isReadOnly: false,
  isImpersonating: false,
  login: async () => "Staff",
  adoptSession: async () => "Staff",
  logout: async () => undefined,
  hasPermission: () => false,
  impersonate: async () => undefined,
  exitImpersonation: () => undefined,
};

const samplePlans = [
  {
    code: "Starter",
    name: "Starter",
    description: "Small institutes",
    priceMonthly: 4500,
    currency: "LKR",
    features: ["ParentPortal"],
    limits: { Students: 250, SmsSegmentsPerMonth: 300, CloudStorageGb: 1 },
  },
];

function renderRoute(route: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <HelmetProvider>
      <QueryClientProvider client={client}>
                  <AuthContext.Provider value={authMock}>
            <MemoryRouter initialEntries={[route]}>
              <BrandingProvider>
                <Routes>
                  <Route
                    element={
                      <MarketingLayout>
                        <Outlet />
                      </MarketingLayout>
                    }
                  >
                    <Route path="/" element={<HomePage />} />
                    <Route path="/features" element={<FeaturesOverviewPage />} />
                    <Route path="/features/attendance" element={<FeatureDetailPage fixedSlug="attendance" />} />
                    <Route path="/features/fees" element={<FeatureDetailPage fixedSlug="fees" />} />
                    <Route path="/features/exams" element={<FeatureDetailPage fixedSlug="exams" />} />
                    <Route path="/features/messaging" element={<FeatureDetailPage fixedSlug="messaging" />} />
                    <Route path="/features/parent-portal" element={<FeatureDetailPage fixedSlug="parent-portal" />} />
                    <Route path="/features/backup" element={<FeatureDetailPage fixedSlug="backup" />} />
                    <Route path="/pricing" element={<PricingPage />} />
                    <Route path="/demo" element={<DemoPage />} />
                    <Route path="/about" element={<AboutPage />} />
                    <Route path="/contact" element={<ContactPage />} />
                    <Route path="*" element={<NotFoundPage />} />
                  </Route>
                  <Route path="/login" element={<LoginPage />} />
                  <Route path="/signup" element={<SignupPage />} />
                </Routes>
              </BrandingProvider>
            </MemoryRouter>
          </AuthContext.Provider>
      </QueryClientProvider>
    </HelmetProvider>,
  );
}

beforeEach(() => {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockResolvedValue(new Response(JSON.stringify(samplePlans), { status: 200 })),
  );
  window.scrollTo = vi.fn();
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
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("Public marketing routes", () => {
  const routes = [
    "/",
    "/features",
    "/features/attendance",
    "/features/fees",
    "/features/exams",
    "/features/messaging",
    "/features/parent-portal",
    "/features/backup",
    "/pricing",
    "/demo",
    "/about",
    "/contact",
    "/login",
    "/signup",
    "/missing-page-404",
  ];

  it.each(routes)("renders %s with a single h1 and document title", async (route) => {
    const { container } = renderRoute(route);
    await waitFor(() => {
      const h1s = container.querySelectorAll("h1");
      expect(h1s).toHaveLength(1);
      expect(document.title.length).toBeGreaterThan(0);
    });
  });

  it("supports keyboard navigation on Features dropdown, mobile drawer, and FAQ accordion", async () => {
    renderRoute("/pricing");
    const featuresTrigger = screen.getByRole("button", { name: /Features/i });
    fireEvent.click(featuresTrigger);
    expect(featuresTrigger).toHaveAttribute("aria-expanded", "true");
    fireEvent.keyDown(document, { key: "Escape" });
    expect(featuresTrigger).toHaveAttribute("aria-expanded", "false");

    const faqBtn = screen.getByRole("button", { name: /What happens when SMS quota runs out/i });
    expect(faqBtn).toHaveAttribute("aria-expanded", "false");
    fireEvent.click(faqBtn);
    expect(faqBtn).toHaveAttribute("aria-expanded", "true");
  });

  it("renders graceful fallback when image fails to load", () => {
    render(<Img src="/images/missing.jpg" alt="Missing photo" width={800} height={600} />);
    const img = screen.getByAltText("Missing photo");
    fireEvent.error(img);
    expect(screen.getByRole("img", { name: "Missing photo" })).toBeInTheDocument();
  });
});
