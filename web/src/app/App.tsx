import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";
import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import { AppShell } from "@/components/layout/AppShell";
import { FeatureProvider } from "@/components/feature-gate";
import { ToastProvider } from "@/components/toast";
import { INSTITUTE_ROLES } from "@/domain/catalog";
import { AcceptInvitePage, ChangePasswordPage, ForgotPasswordPage, LoginPage, ResetPasswordPage } from "@/features/auth/AuthPages";
import { AuthProvider } from "@/features/auth/AuthProvider";
import { GuestOnly, RequireAuth, RequireRole } from "@/features/auth/guards";
import { BrandingPage } from "@/features/branding/BrandingPage";
import { BrandingProvider } from "@/features/branding/BrandingProvider";
import { DashboardPage } from "@/features/dashboard/DashboardPage";
import { FeaturesPage } from "@/features/features/FeaturesPage";
import { ParentHomePage } from "@/features/parent/ParentHomePage";
import { PlansPage } from "@/features/super-admin/PlansPage";
import { TenantDetailPage } from "@/features/super-admin/TenantDetailPage";
import { TenantsPage } from "@/features/super-admin/TenantsPage";

export default function App() {
  const [client] = useState(() => new QueryClient({ defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } } }));

  return (
    <QueryClientProvider client={client}>
      <ToastProvider>
        <BrowserRouter>
          <AuthProvider>
            <BrandingProvider>
              <FeatureProvider>
                <Routes>
                  <Route path="/login" element={<GuestOnly><LoginPage /></GuestOnly>} />
                  <Route path="/forgot-password" element={<GuestOnly><ForgotPasswordPage /></GuestOnly>} />
                  <Route path="/reset-password" element={<GuestOnly><ResetPasswordPage /></GuestOnly>} />
                  <Route path="/accept-invite" element={<GuestOnly><AcceptInvitePage /></GuestOnly>} />
                  <Route element={<RequireAuth><AppShell /></RequireAuth>}>
                    <Route path="/account/password" element={<ChangePasswordPage />} />
                    <Route path="/app" element={<RequireRole roles={INSTITUTE_ROLES}><DashboardPage /></RequireRole>} />
                    <Route path="/app/settings/features" element={<RequireRole roles={INSTITUTE_ROLES}><FeaturesPage /></RequireRole>} />
                    <Route path="/app/settings/branding" element={<RequireRole roles={INSTITUTE_ROLES}><BrandingPage /></RequireRole>} />
                    <Route path="/admin/tenants" element={<RequireRole roles={["SuperAdmin"]}><TenantsPage /></RequireRole>} />
                    <Route path="/admin/tenants/:id" element={<RequireRole roles={["SuperAdmin"]}><TenantDetailPage /></RequireRole>} />
                    <Route path="/admin/plans" element={<RequireRole roles={["SuperAdmin"]}><PlansPage /></RequireRole>} />
                    <Route path="/admin/branding" element={<RequireRole roles={["SuperAdmin"]}><BrandingPage platform /></RequireRole>} />
                    <Route path="/parent" element={<RequireRole roles={["Parent"]}><ParentHomePage /></RequireRole>} />
                  </Route>
                  <Route path="*" element={<Navigate to="/login" replace />} />
                </Routes>
              </FeatureProvider>
            </BrandingProvider>
          </AuthProvider>
        </BrowserRouter>
      </ToastProvider>
    </QueryClientProvider>
  );
}
