import { render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, expect, it } from "vitest";
import i18n from "@/i18n";
import type { AuthUser } from "@/lib/types";
import { AuthContext, type AuthContextValue } from "./AuthProvider";
import { RequireAuth, RequireRole } from "./guards";

function user(role: AuthUser["role"]): AuthUser {
  return {
    id: "1",
    tenantId: role === "SuperAdmin" ? null : "tenant",
    email: "a@eduflow.lk",
    fullName: "Amal Perera",
    role,
    permissions: [],
    mustChangePassword: false,
  };
}

function value(account: AuthUser | null): AuthContextValue {
  return {
    user: account,
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
}

function renderAt(path: string, account: AuthUser | null) {
  return render(
    <I18nextProvider i18n={i18n}>
      <MemoryRouter initialEntries={[path]}>
        <AuthContext.Provider value={value(account)}>
          <Routes>
            <Route path="/login" element={<p>Login screen</p>} />
            <Route
              path="/app"
              element={
                <RequireAuth>
                  <RequireRole roles={["InstituteAdmin", "Staff", "Teacher"]}>
                    <p>Dashboard</p>
                  </RequireRole>
                </RequireAuth>
              }
            />
            <Route
              path="/admin/tenants"
              element={
                <RequireAuth>
                  <RequireRole roles={["SuperAdmin"]}>
                    <p>Admin home</p>
                  </RequireRole>
                </RequireAuth>
              }
            />
          </Routes>
        </AuthContext.Provider>
      </MemoryRouter>
    </I18nextProvider>,
  );
}

describe("route guards", () => {
  it("sends anonymous visitors to sign in", () => {
    renderAt("/app", null);
    expect(screen.getByText("Login screen")).toBeInTheDocument();
  });

  it("lets staff open the institute app", () => {
    renderAt("/app", user("Staff"));
    expect(screen.getByText("Dashboard")).toBeInTheDocument();
  });

  it("blocks staff from the platform console", () => {
    renderAt("/admin/tenants", user("Staff"));
    expect(screen.getByText("You cannot open this page")).toBeInTheDocument();
  });

  it("lets a super admin open the platform console", () => {
    renderAt("/admin/tenants", user("SuperAdmin"));
    expect(screen.getByText("Admin home")).toBeInTheDocument();
  });
});
