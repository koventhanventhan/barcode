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
import { JoinPage } from "@/features/attendance/JoinPage";
import { RegisterPage } from "@/features/attendance/RegisterPage";
import { ReportsPage } from "@/features/attendance/ReportsPage";
import { ScannerPage } from "@/features/attendance/ScannerPage";
import { BatchesPage } from "@/features/academics/BatchesPage";
import { ClassesPage } from "@/features/academics/ClassesPage";
import { LevelsPage } from "@/features/academics/LevelsPage";
import { SubjectsPage } from "@/features/academics/SubjectsPage";
import { DashboardPage } from "@/features/dashboard/DashboardPage";
import { StaffPage } from "@/features/staff/StaffPage";
import { ImportStudentsPage } from "@/features/students/ImportStudentsPage";
import { StudentFormPage } from "@/features/students/StudentFormPage";
import { StudentsPage } from "@/features/students/StudentsPage";
import { FeaturesPage } from "@/features/features/FeaturesPage";
import { MessageLogPage } from "@/features/features/MessageLogPage";
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
                  <Route path="/join" element={<JoinPage />} />
                  <Route path="/login" element={<GuestOnly><LoginPage /></GuestOnly>} />
                  <Route path="/forgot-password" element={<GuestOnly><ForgotPasswordPage /></GuestOnly>} />
                  <Route path="/reset-password" element={<GuestOnly><ResetPasswordPage /></GuestOnly>} />
                  <Route path="/accept-invite" element={<GuestOnly><AcceptInvitePage /></GuestOnly>} />
                  <Route element={<RequireAuth><AppShell /></RequireAuth>}>
                    <Route path="/account/password" element={<ChangePasswordPage />} />
                    <Route path="/app" element={<RequireRole roles={INSTITUTE_ROLES}><DashboardPage /></RequireRole>} />
                    <Route path="/app/students" element={<RequireRole roles={INSTITUTE_ROLES}><StudentsPage /></RequireRole>} />
                    <Route path="/app/students/new" element={<RequireRole roles={INSTITUTE_ROLES}><StudentFormPage /></RequireRole>} />
                    <Route path="/app/students/import" element={<RequireRole roles={INSTITUTE_ROLES}><ImportStudentsPage /></RequireRole>} />
                    <Route path="/app/students/:id" element={<RequireRole roles={INSTITUTE_ROLES}><StudentFormPage /></RequireRole>} />
                    <Route path="/app/academics/levels" element={<RequireRole roles={INSTITUTE_ROLES}><LevelsPage /></RequireRole>} />
                    <Route path="/app/academics/batches" element={<RequireRole roles={INSTITUTE_ROLES}><BatchesPage /></RequireRole>} />
                    <Route path="/app/academics/subjects" element={<RequireRole roles={INSTITUTE_ROLES}><SubjectsPage /></RequireRole>} />
                    <Route path="/app/academics/classes" element={<RequireRole roles={INSTITUTE_ROLES}><ClassesPage /></RequireRole>} />
                    <Route path="/app/staff" element={<RequireRole roles={INSTITUTE_ROLES}><StaffPage /></RequireRole>} />
                    <Route path="/app/attendance" element={<RequireRole roles={INSTITUTE_ROLES}><ScannerPage /></RequireRole>} />
                    <Route path="/app/attendance/register" element={<RequireRole roles={INSTITUTE_ROLES}><RegisterPage /></RequireRole>} />
                    <Route path="/app/attendance/reports" element={<RequireRole roles={INSTITUTE_ROLES}><ReportsPage /></RequireRole>} />
                    <Route path="/app/settings/features" element={<RequireRole roles={INSTITUTE_ROLES}><FeaturesPage /></RequireRole>} />
                    <Route path="/app/settings/messages" element={<RequireRole roles={INSTITUTE_ROLES}><MessageLogPage /></RequireRole>} />
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
