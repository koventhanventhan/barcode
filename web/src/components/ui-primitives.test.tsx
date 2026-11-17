import { fireEvent, render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import { describe, expect, it, vi } from "vitest";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { EmptyState } from "@/components/empty-state";
import { PermissionGate } from "@/components/permission-gate";
import { StatCard } from "@/components/stat-card";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { AuthContext, type AuthContextValue } from "@/features/auth/AuthProvider";
import i18n from "@/i18n";

function authValue(permissions: string[]): AuthContextValue {
  return {
    user: {
      id: "1",
      tenantId: "t1",
      email: "admin@demo.eduflow.lk",
      fullName: "Admin User",
      role: "InstituteAdmin",
      permissions,
      mustChangePassword: false,
    },
    isReady: true,
    isReadOnly: false,
    isImpersonating: false,
    login: async () => "InstituteAdmin",
    adoptSession: async () => "InstituteAdmin",
    logout: async () => undefined,
    hasPermission: (p) => permissions.includes(p),
    impersonate: async () => undefined,
    exitImpersonation: () => undefined,
  };
}

describe("UI primitives", () => {
  it("renders EmptyState with optional description and action", () => {
    render(
      <EmptyState
        title="No records"
        description="Add a record to get started."
        action={<button type="button">Add record</button>}
      />,
    );
    expect(screen.getByText("No records")).toBeInTheDocument();
    expect(screen.getByText("Add a record to get started.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Add record" })).toBeInTheDocument();

    render(<EmptyState title="Minimal" />);
    expect(screen.getByText("Minimal")).toBeInTheDocument();
  });

  it("renders StatCard with optional hint", () => {
    render(<StatCard label="Attendance" value="94%" hint="Today across 4 classes" />);
    expect(screen.getByText("Attendance")).toBeInTheDocument();
    expect(screen.getByText("94%")).toBeInTheDocument();
    expect(screen.getByText("Today across 4 classes")).toBeInTheDocument();
  });

  it("renders ConfirmDialog and handles cancel and confirm", () => {
    const onConfirm = vi.fn();
    const onOpenChange = vi.fn();
    render(
      <I18nextProvider i18n={i18n}>
        <ConfirmDialog
          open
          title="Delete item"
          description="This cannot be undone."
          confirmLabel="Delete"
          destructive
          onConfirm={onConfirm}
          onOpenChange={onOpenChange}
        />
      </I18nextProvider>,
    );

    expect(screen.getByText("Delete item")).toBeInTheDocument();
    expect(screen.getByText("This cannot be undone.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Delete" }));
    expect(onConfirm).toHaveBeenCalledTimes(1);
    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("toggles Switch and accepts Textarea input", () => {
    const onCheckedChange = vi.fn();
    render(
      <div>
        <Switch aria-label="Enable portal" checked={false} onCheckedChange={onCheckedChange} />
        <Textarea aria-label="Notes" defaultValue="Initial" />
      </div>,
    );

    fireEvent.click(screen.getByRole("switch", { name: "Enable portal" }));
    expect(onCheckedChange).toHaveBeenCalledWith(true);
    expect(screen.getByLabelText("Notes")).toHaveValue("Initial");
  });

  it("enforces PermissionGate based on granted permissions", () => {
    render(
      <AuthContext.Provider value={authValue(["students.view"])}>
        <PermissionGate permission="students.view" fallback={<p>Denied view</p>}>
          <p>Allowed view</p>
        </PermissionGate>
        <PermissionGate permission="fees.collect" fallback={<p>Denied fees</p>}>
          <p>Allowed fees</p>
        </PermissionGate>
      </AuthContext.Provider>,
    );

    expect(screen.getByText("Allowed view")).toBeInTheDocument();
    expect(screen.getByText("Denied fees")).toBeInTheDocument();
    expect(screen.queryByText("Allowed fees")).not.toBeInTheDocument();
  });
});
