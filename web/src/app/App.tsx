import { HelmetProvider } from "react-helmet-async";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Suspense, lazy, useState } from "react";
import { BrowserRouter, Outlet, Route, Routes } from "react-router-dom";
import { FeatureProvider } from "@/components/feature-gate";
import { PageSkeleton } from "@/components/skeletons";
import { ToastProvider } from "@/components/toast";
import { INSTITUTE_ROLES } from "@/domain/catalog";
import { AuthProvider } from "@/features/auth/AuthProvider";
import { GuestOnly, RequireAuth, RequireRole } from "@/features/auth/guards";
import { BrandingProvider } from "@/features/branding/BrandingProvider";
import { MarketingLayout } from "@/features/marketing/MarketingLayout";

const HomePage = lazy(() => import("@/features/marketing/pages/HomePage"));
const FeaturesOverviewPage = lazy(() => import("@/features/marketing/pages/FeaturesOverviewPage"));
const FeatureDetailPage = lazy(() => import("@/features/marketing/pages/FeatureDetailPage"));
const PricingPage = lazy(() => import("@/features/marketing/pages/PricingPage"));
const DemoPage = lazy(() => import("@/features/marketing/pages/DemoPage"));
const AboutPage = lazy(() => import("@/features/marketing/pages/AboutPage"));
const ContactPage = lazy(() => import("@/features/marketing/pages/ContactPage"));
const NotFoundPage = lazy(() => import("@/features/marketing/pages/NotFoundPage"));
const SignupPage = lazy(() => import("@/features/marketing/SignupPage").then((m) => ({ default: m.SignupPage })));

const AppShell = lazy(() => import("@/components/layout/AppShell").then((m) => ({ default: m.AppShell })));
const ParentShell = lazy(() => import("@/features/parent/ParentShell").then((m) => ({ default: m.ParentShell })));
const LoginPage = lazy(() => import("@/features/auth/AuthPages").then((m) => ({ default: m.LoginPage })));
const ForgotPasswordPage = lazy(() => import("@/features/auth/AuthPages").then((m) => ({ default: m.ForgotPasswordPage })));
const ResetPasswordPage = lazy(() => import("@/features/auth/AuthPages").then((m) => ({ default: m.ResetPasswordPage })));
const AcceptInvitePage = lazy(() => import("@/features/auth/AuthPages").then((m) => ({ default: m.AcceptInvitePage })));
const ChangePasswordPage = lazy(() => import("@/features/auth/AuthPages").then((m) => ({ default: m.ChangePasswordPage })));
const JoinPage = lazy(() => import("@/features/attendance/JoinPage").then((m) => ({ default: m.JoinPage })));
const RegisterPage = lazy(() => import("@/features/attendance/RegisterPage").then((m) => ({ default: m.RegisterPage })));
const ReportsPage = lazy(() => import("@/features/attendance/ReportsPage").then((m) => ({ default: m.ReportsPage })));
const ScannerPage = lazy(() => import("@/features/attendance/ScannerPage").then((m) => ({ default: m.ScannerPage })));
const BatchesPage = lazy(() => import("@/features/academics/BatchesPage").then((m) => ({ default: m.BatchesPage })));
const ClassesPage = lazy(() => import("@/features/academics/ClassesPage").then((m) => ({ default: m.ClassesPage })));
const LevelsPage = lazy(() => import("@/features/academics/LevelsPage").then((m) => ({ default: m.LevelsPage })));
const SubjectsPage = lazy(() => import("@/features/academics/SubjectsPage").then((m) => ({ default: m.SubjectsPage })));
const TimetablePage = lazy(() => import("@/features/academics/TimetablePage").then((m) => ({ default: m.TimetablePage })));
const DashboardPage = lazy(() => import("@/features/dashboard/DashboardPage").then((m) => ({ default: m.DashboardPage })));
const FeesPage = lazy(() => import("@/features/fees/FeesPage").then((m) => ({ default: m.FeesPage })));
const ExamsPage = lazy(() => import("@/features/exams/ExamsPage").then((m) => ({ default: m.ExamsPage })));
const PaymentStatusPage = lazy(() => import("@/features/fees/PaymentStatusPage").then((m) => ({ default: m.PaymentStatusPage })));
const BackupsAndCleanupPage = lazy(() => import("@/features/operations/BackupsAndCleanupPage").then((m) => ({ default: m.BackupsAndCleanupPage })));
const StaffPage = lazy(() => import("@/features/staff/StaffPage").then((m) => ({ default: m.StaffPage })));
const ImportStudentsPage = lazy(() => import("@/features/students/ImportStudentsPage").then((m) => ({ default: m.ImportStudentsPage })));
const StudentFormPage = lazy(() => import("@/features/students/StudentFormPage").then((m) => ({ default: m.StudentFormPage })));
const StudentsPage = lazy(() => import("@/features/students/StudentsPage").then((m) => ({ default: m.StudentsPage })));
const FeaturesPage = lazy(() => import("@/features/features/FeaturesPage").then((m) => ({ default: m.FeaturesPage })));
const MessageLogPage = lazy(() => import("@/features/features/MessageLogPage").then((m) => ({ default: m.MessageLogPage })));
const BrandingPage = lazy(() => import("@/features/branding/BrandingPage").then((m) => ({ default: m.BrandingPage })));
const ParentsAdminPage = lazy(() => import("@/features/parent/ParentsAdminPage").then((m) => ({ default: m.ParentsAdminPage })));
const ParentDashboardPage = lazy(() => import("@/features/parent/ParentPages").then((m) => ({ default: m.ParentDashboardPage })));
const ParentAttendancePage = lazy(() => import("@/features/parent/ParentPages").then((m) => ({ default: m.ParentAttendancePage })));
const ParentClassesPage = lazy(() => import("@/features/parent/ParentPages").then((m) => ({ default: m.ParentClassesPage })));
const ParentResultsPage = lazy(() => import("@/features/parent/ParentPages").then((m) => ({ default: m.ParentResultsPage })));
const ParentFeesPage = lazy(() => import("@/features/parent/ParentPages").then((m) => ({ default: m.ParentFeesPage })));
const ParentMessagesPage = lazy(() => import("@/features/parent/ParentPages").then((m) => ({ default: m.ParentMessagesPage })));
const PlatformDashboardPage = lazy(() => import("@/features/super-admin/PlatformDashboardPage").then((m) => ({ default: m.PlatformDashboardPage })));
const PlansPage = lazy(() => import("@/features/super-admin/PlansPage").then((m) => ({ default: m.PlansPage })));
const TenantDetailPage = lazy(() => import("@/features/super-admin/TenantDetailPage").then((m) => ({ default: m.TenantDetailPage })));
const TenantsPage = lazy(() => import("@/features/super-admin/TenantsPage").then((m) => ({ default: m.TenantsPage })));

function MarketingRouteShell() {
  return (
    <MarketingLayout>
      <Suspense fallback={<div className="mx-auto min-h-[90vh] max-w-6xl px-4 py-12"><PageSkeleton /></div>}>
        <Outlet />
      </Suspense>
    </MarketingLayout>
  );
}

export default function App() {
  const [client] = useState(() => new QueryClient({ defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } } }));

  return (
    <HelmetProvider>
      <QueryClientProvider client={client}>
        <ToastProvider>
          <BrowserRouter>
            <AuthProvider>
              <BrandingProvider>
                <FeatureProvider>
                  <Suspense fallback={<div className="mx-auto min-h-screen max-w-6xl px-4 py-12"><PageSkeleton /></div>}>
                    <Routes>
                      <Route element={<MarketingRouteShell />}>
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
                      <Route path="/signup" element={<SignupPage />} />
                      <Route path="/join" element={<JoinPage />} />
                      <Route path="/pay/:orderId" element={<PaymentStatusPage />} />
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
                        <Route path="/app/academics/timetable" element={<RequireRole roles={INSTITUTE_ROLES}><TimetablePage /></RequireRole>} />
                        <Route path="/app/staff" element={<RequireRole roles={INSTITUTE_ROLES}><StaffPage /></RequireRole>} />
                        <Route path="/app/parents" element={<RequireRole roles={INSTITUTE_ROLES}><ParentsAdminPage /></RequireRole>} />
                        <Route path="/app/attendance" element={<RequireRole roles={INSTITUTE_ROLES}><ScannerPage /></RequireRole>} />
                        <Route path="/app/attendance/register" element={<RequireRole roles={INSTITUTE_ROLES}><RegisterPage /></RequireRole>} />
                        <Route path="/app/attendance/reports" element={<RequireRole roles={INSTITUTE_ROLES}><ReportsPage /></RequireRole>} />
                        <Route path="/app/fees" element={<RequireRole roles={INSTITUTE_ROLES}><FeesPage /></RequireRole>} />
                        <Route path="/app/exams" element={<RequireRole roles={INSTITUTE_ROLES}><ExamsPage /></RequireRole>} />
                        <Route path="/app/fees/pay/:orderId" element={<RequireRole roles={INSTITUTE_ROLES}><PaymentStatusPage /></RequireRole>} />
                        <Route path="/app/settings/features" element={<RequireRole roles={INSTITUTE_ROLES}><FeaturesPage /></RequireRole>} />
                        <Route path="/app/settings/messages" element={<RequireRole roles={INSTITUTE_ROLES}><MessageLogPage /></RequireRole>} />
                        <Route path="/app/settings/backups" element={<RequireRole roles={INSTITUTE_ROLES}><BackupsAndCleanupPage /></RequireRole>} />
                        <Route path="/app/settings/branding" element={<RequireRole roles={INSTITUTE_ROLES}><BrandingPage /></RequireRole>} />
                        <Route path="/admin/dashboard" element={<RequireRole roles={["SuperAdmin"]}><PlatformDashboardPage /></RequireRole>} />
                        <Route path="/admin/tenants" element={<RequireRole roles={["SuperAdmin"]}><TenantsPage /></RequireRole>} />
                        <Route path="/admin/tenants/:id" element={<RequireRole roles={["SuperAdmin"]}><TenantDetailPage /></RequireRole>} />
                        <Route path="/admin/plans" element={<RequireRole roles={["SuperAdmin"]}><PlansPage /></RequireRole>} />
                        <Route path="/admin/branding" element={<RequireRole roles={["SuperAdmin"]}><BrandingPage platform /></RequireRole>} />
                      </Route>
                      <Route element={<RequireAuth><ParentShell /></RequireAuth>}>
                        <Route path="/parent" element={<RequireRole roles={["Parent"]}><ParentDashboardPage /></RequireRole>} />
                        <Route path="/parent/attendance" element={<RequireRole roles={["Parent"]}><ParentAttendancePage /></RequireRole>} />
                        <Route path="/parent/classes" element={<RequireRole roles={["Parent"]}><ParentClassesPage /></RequireRole>} />
                        <Route path="/parent/results" element={<RequireRole roles={["Parent"]}><ParentResultsPage /></RequireRole>} />
                        <Route path="/parent/fees" element={<RequireRole roles={["Parent"]}><ParentFeesPage /></RequireRole>} />
                        <Route path="/parent/messages" element={<RequireRole roles={["Parent"]}><ParentMessagesPage /></RequireRole>} />
                      </Route>
                    </Routes>
                  </Suspense>
                </FeatureProvider>
              </BrandingProvider>
            </AuthProvider>
          </BrowserRouter>
        </ToastProvider>
      </QueryClientProvider>
    </HelmetProvider>
  );
}
