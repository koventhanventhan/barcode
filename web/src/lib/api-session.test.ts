import { afterEach, describe, expect, it, vi } from "vitest";
import { passwordSchema } from "@/features/auth/password";
import { ApiError, api, refreshAccessToken } from "@/lib/api";
import {
  clearActorBackup,
  getAccessToken,
  getSession,
  getTenantSlug,
  getThemeOverride,
  readActorBackup,
  saveActorBackup,
  setSession,
  setTenantSlug,
  setThemeOverride,
  subscribeSession,
  subscribeTenantSlug,
} from "@/lib/session";

const sampleUser = {
  id: "u1",
  tenantId: "t1",
  email: "admin@demo.admino.lk",
  fullName: "Admin",
  role: "InstituteAdmin" as const,
  permissions: ["students.view"],
  mustChangePassword: false,
};

afterEach(() => {
  setSession(null);
  setTenantSlug("");
  clearActorBackup();
  vi.restoreAllMocks();
});

describe("session helpers", () => {
  it("manages session, tenant slug, theme, and actor backup", () => {
    const onSession = vi.fn();
    const onSlug = vi.fn();
    const unsubSession = subscribeSession(onSession);
    const unsubSlug = subscribeTenantSlug(onSlug);

    setSession({ accessToken: "tok-1", user: sampleUser, isReadOnly: false, isImpersonating: false });
    expect(getSession()?.accessToken).toBe("tok-1");
    expect(getAccessToken()).toBe("tok-1");
    expect(onSession).toHaveBeenCalledTimes(1);

    setTenantSlug("  DEMO ");
    expect(getTenantSlug()).toBe("demo");
    expect(onSlug).toHaveBeenCalledTimes(1);

    setThemeOverride("dark");
    expect(getThemeOverride()).toBe("dark");

    saveActorBackup({ accessToken: "sa-tok", user: sampleUser, tenantSlug: "demo" });
    expect(readActorBackup()?.accessToken).toBe("sa-tok");
    clearActorBackup();
    expect(readActorBackup()).toBeNull();

    unsubSession();
    unsubSlug();
  });
});

describe("api client", () => {
  it("sends auth headers, retries on 401 after refresh, and maps ApiError", async () => {
    setSession({ accessToken: "expired", user: sampleUser, isReadOnly: false, isImpersonating: false });
    setTenantSlug("demo");

    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ code: "UNAUTHORIZED" }), { status: 401 }))
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            accessToken: "fresh-token",
            refreshToken: "rt",
            user: sampleUser,
            isReadOnly: false,
            isImpersonating: false,
          }),
          { status: 200 },
        ),
      )
      .mockResolvedValueOnce(new Response(JSON.stringify({ ok: true }), { status: 200 }));

    vi.stubGlobal("fetch", fetchMock);

    const result = await api<{ ok: boolean }>("/api/students", { method: "POST", body: JSON.stringify({ name: "A" }) });
    expect(result.ok).toBe(true);
    expect(fetchMock).toHaveBeenCalledTimes(3);

    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ code: "FEATURE_DISABLED", feature: "ParentPortal", message: "Off" }), { status: 403 }),
      ),
    );
    await expect(api("/api/parent/dashboard")).rejects.toMatchObject({
      name: "Error",
      status: 403,
      code: "FEATURE_DISABLED",
      feature: "ParentPortal",
    });

    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 204 })));
    expect(await api("/api/empty")).toBeUndefined();

    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 401 })));
    expect(await refreshAccessToken()).toBeNull();
    expect(new ApiError(400, "Bad").status).toBe(400);
  });
});

describe("passwordSchema", () => {
  it("requires length, case, digit, and symbol", () => {
    const schema = passwordSchema("Weak password");
    expect(schema.safeParse("Strong1!").success).toBe(true);
    expect(schema.safeParse("weak").success).toBe(false);
    expect(schema.safeParse("noupper1!").success).toBe(false);
    expect(schema.safeParse("NOLOWER1!").success).toBe(false);
    expect(schema.safeParse("NoDigit!a").success).toBe(false);
    expect(schema.safeParse("NoSymbol1a").success).toBe(false);
  });
});
