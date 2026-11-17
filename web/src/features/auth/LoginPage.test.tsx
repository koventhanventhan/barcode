import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import i18n from "@/i18n";
import { LoginPage } from "./AuthPages";

const { login, setInstitute } = vi.hoisted(() => ({
  login: vi.fn(),
  setInstitute: vi.fn(),
}));

vi.mock("@/features/auth/AuthProvider", () => ({
  useAuth: () => ({ login }),
  homeFor: (role: string) => (role === "SuperAdmin" ? "/admin/tenants" : "/app"),
}));

vi.mock("@/features/branding/BrandingProvider", () => ({
  useBranding: () => ({
    branding: {
      instituteDisplayName: "EduFlow",
      tagline: null,
      logoUrl: null,
      loginBackgroundUrl: null,
      removeProductBranding: false,
    },
    institute: "",
    setInstitute,
  }),
}));

describe("LoginPage", () => {
  beforeEach(() => {
    login.mockReset();
    login.mockResolvedValue("SuperAdmin");
    setInstitute.mockReset();
  });

  it("lets a platform operator submit with a blank institute code", async () => {
    const user = userEvent.setup();
    render(
      <I18nextProvider i18n={i18n}>
        <MemoryRouter initialEntries={["/login"]}>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/admin/tenants" element={<p>Admin home</p>} />
          </Routes>
        </MemoryRouter>
      </I18nextProvider>,
    );

    await user.type(screen.getByLabelText("Email"), "superadmin@eduflow.lk");
    await user.type(screen.getByLabelText("Password"), "SuperAdmin@123!");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    expect(screen.queryByText("Required")).not.toBeInTheDocument();
    expect(setInstitute).toHaveBeenCalledWith("");
    expect(login).toHaveBeenCalledWith("superadmin@eduflow.lk", "SuperAdmin@123!", "");
    expect(await screen.findByText("Admin home")).toBeInTheDocument();
  });
});
