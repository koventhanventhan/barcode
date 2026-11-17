import fs from "node:fs";
import path from "node:path";
import { expect, test, type Page } from "@playwright/test";

const screenshotsDir = path.resolve(__dirname, "../../docs/screenshots");

async function loginViaUi(page: Page, options: { institute: string; email?: string; phone?: string; password: string; mode?: "staff" | "parent" }) {
  await page.goto("/login");
  if (options.mode === "parent") {
    await page.getByRole("button", { name: "Parent" }).click();
  }
  await page.locator("#institute").fill(options.institute);
  if (options.mode === "parent") {
    await page.locator("#phone").fill(options.phone ?? "");
  } else {
    await page.locator("#email").fill(options.email ?? "");
  }
  await page.locator("#password").fill(options.password);
  await page.getByRole("button", { name: "Sign in" }).click();
}

async function logoutViaUi(page: Page) {
  await page.getByRole("button", { name: /Log out/i }).click();
  await expect(page).toHaveURL(/\/login$/);
}

test.describe.serial("EduFlow Phase 11 E2E journeys", () => {
  test("1. signup -> add student -> print ID card PDF", async ({ page }) => {
    const suffix = Date.now().toString().slice(-6);
    const subdomain = `e2e-growth-${suffix}`;

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto("/");
    fs.mkdirSync(screenshotsDir, { recursive: true });
    await page.screenshot({ path: path.join(screenshotsDir, "landing.png"), fullPage: false });

    await page.goto("/signup");
    await page.locator("#instituteName").fill(`Colombo Academy ${suffix}`);
    await page.locator("#subdomain").fill(subdomain);
    await page.locator("#adminFullName").fill("Kamal Jayawardena");
    await page.locator("#adminEmail").fill(`kamal-${suffix}@eduflow.lk`);
    await page.locator("#phone").fill("0771112233");
    await page.locator("#adminPassword").fill("StrongPass@123!");
    await page.locator("#planCode").selectOption("Growth");
    await page.getByRole("button", { name: "Start a 14-day trial" }).click();

    await expect(page).toHaveURL(/\/app$/);
    await expect(page.getByRole("heading", { name: /Dashboard/i })).toBeVisible();

    await page.goto("/app/students/new");
    await expect(page.locator("#educationLevelId option").first()).toBeAttached();
    await page.locator("#fullName").fill("Ruwan Wijesinghe");
    await page.locator("#guardianName").fill("Saman Wijesinghe");
    await page.locator("#guardianPhone").fill("0774455667");
    await page.locator("#address").fill("18 Ward Place, Colombo 07");
    await page.getByRole("button", { name: "Save" }).click();

    await expect(page).toHaveURL(/\/app\/students\/[0-9a-f-]+$/i);
    await expect(page.getByRole("heading", { name: "Ruwan Wijesinghe" })).toBeVisible();

    const downloadPromise = page.waitForEvent("download");
    await page.getByRole("button", { name: "ID card" }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toMatch(/-id-card\.pdf$/);
    const filePath = await download.path();
    expect(filePath).toBeTruthy();
    const header = fs.readFileSync(filePath!).subarray(0, 5).toString("utf8");
    expect(header).toBe("%PDF-");
  });

  test("2. start class -> scan barcode -> parent SMS in log -> stop class -> absent messages", async ({ page }) => {
    await loginViaUi(page, {
      institute: "demo",
      email: "admin@demo.eduflow.lk",
      password: "DemoAdmin@123!",
    });
    await expect(page).toHaveURL(/\/app$/);
    await page.screenshot({ path: path.join(screenshotsDir, "dashboard.png"), fullPage: false });

    await page.goto("/app/attendance");
    const select = page.locator("main select").first();
    await expect(select).toContainText("Combined Mathematics");
    const optionValue = await select.locator("option", { hasText: "Combined Mathematics" }).first().getAttribute("value");
    expect(optionValue).toBeTruthy();
    await select.selectOption(optionValue!);

    await page.getByRole("button", { name: "Start class" }).click();
    await expect(page.getByRole("button", { name: "Stop class" })).toBeEnabled();

    const scanInput = page.getByLabel("Student code");
    await scanInput.fill("S000001");
    await scanInput.press("Enter");
    await expect(page.locator("main [role='status']").first()).toContainText("Nimal Perera");
    await page.screenshot({ path: path.join(screenshotsDir, "scanner.png"), fullPage: false });

    await page.getByRole("button", { name: "Stop class" }).click();
    await expect(page.getByText(/\b[1-9]\d*\s+absent\b/i)).toBeVisible();

    await page.goto("/app/settings/messages");
    await expect(page.getByRole("heading", { name: "Message log" })).toBeVisible();
    await expect(page.locator("table").getByText("Attendance present").first()).toBeVisible();
    await expect(page.locator("table").getByText("Attendance absent").first()).toBeVisible();
  });

  test("3. invoice -> PayHere sandbox -> official receipt PDF", async ({ page }) => {
    await loginViaUi(page, {
      institute: "demo",
      email: "admin@demo.eduflow.lk",
      password: "DemoAdmin@123!",
    });
    await expect(page).toHaveURL(/\/app$/);

    await page.goto("/app/fees");
    await page.getByRole("tab", { name: "Invoices & payments" }).click();

    const pendingRow = page.locator("tr", { hasText: "INV-000002" });
    await expect(pendingRow).toBeVisible();
    await pendingRow.getByRole("button", { name: "PayHere" }).click();

    await expect(page).toHaveURL(/\/app\/fees\/pay\/.+$/);
    await expect(page.getByRole("button", { name: "Complete sandbox payment" })).toBeVisible();
    await page.getByRole("button", { name: "Complete sandbox payment" }).click();

    await expect(page.getByText("Success", { exact: true })).toBeVisible();
    const downloadPromise = page.waitForEvent("download");
    await page.getByRole("button", { name: /Download official receipt/i }).click();
    const download = await downloadPromise;
    expect(download.suggestedFilename()).toMatch(/\.pdf$/);
    const filePath = await download.path();
    expect(fs.readFileSync(filePath!).subarray(0, 5).toString("utf8")).toBe("%PDF-");
  });

  test("4. switch OFF parent portal -> parent blocked, switch ON -> works", async ({ page }) => {
    const parentPhone = "0778899001";
    const parentPassword = "ParentPass@123!";

    await loginViaUi(page, {
      institute: "demo",
      email: "admin@demo.eduflow.lk",
      password: "DemoAdmin@123!",
    });
    await expect(page).toHaveURL(/\/app$/);

    // Create a parent account with an explicit password linked to Nimal Perera
    await page.goto("/app/parents");
    await page.locator("#parent-name").fill("Sunil Perera");
    await page.locator("#parent-phone").fill(parentPhone);
    await page.getByLabel(/Send an invite by SMS/i).uncheck();
    await page.locator("#parent-password").fill(parentPassword);
    await page.getByLabel(/Nimal Perera/i).check();
    await page.locator("form").getByRole("button", { name: "Add parent" }).click();
    await expect(page.getByText("Sunil Perera").first()).toBeVisible();

    // Switch OFF Parent portal
    await page.goto("/app/settings/features");
    const portalSwitch = page.getByRole("switch", { name: "Parent portal" });
    await expect(portalSwitch).toHaveAttribute("aria-checked", "true");
    await portalSwitch.click();
    await expect(portalSwitch).toHaveAttribute("aria-checked", "false");
    await logoutViaUi(page);

    // Parent login must be blocked with friendly disabled message
    await loginViaUi(page, {
      institute: "demo",
      mode: "parent",
      phone: parentPhone,
      password: parentPassword,
    });
    await expect(page.getByRole("heading", { name: "Parent portal is turned off" })).toBeVisible();

    // Admin switches Parent portal back ON
    await page.getByRole("button", { name: "Back to sign in" }).click();
    await loginViaUi(page, {
      institute: "demo",
      email: "admin@demo.eduflow.lk",
      password: "DemoAdmin@123!",
    });
    await expect(page).toHaveURL(/\/app$/);
    await page.goto("/app/settings/features");
    const portalSwitchOn = page.getByRole("switch", { name: "Parent portal" });
    await portalSwitchOn.click();
    await expect(portalSwitchOn).toHaveAttribute("aria-checked", "true");
    await logoutViaUi(page);

    // Parent can now log in and view child dashboard (mobile viewport 360px check)
    await page.setViewportSize({ width: 360, height: 800 });
    await loginViaUi(page, {
      institute: "demo",
      mode: "parent",
      phone: parentPhone,
      password: parentPassword,
    });
    await expect(page).toHaveURL(/\/parent$/);
    await expect(page.getByRole("heading", { name: "Nimal Perera" })).toBeVisible();
    await page.screenshot({ path: path.join(screenshotsDir, "parent-mobile.png"), fullPage: false });
    await page.setViewportSize({ width: 1440, height: 900 });
  });

  test("5. branding color change reflects on login page", async ({ page, browser }) => {
    await loginViaUi(page, {
      institute: "demo",
      email: "admin@demo.eduflow.lk",
      password: "DemoAdmin@123!",
    });
    await expect(page).toHaveURL(/\/app$/);

    await page.goto("/app/settings/branding");
    const primaryPicker = page.getByLabel("Primary color");
    const hexInput = primaryPicker.locator("..").locator("input:not([type='color'])");
    await hexInput.fill("#2A6F97");
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByText(/Branding saved/i)).toBeVisible();

    // Fresh context simulates a visitor loading the branded login page without prior cache
    const freshContext = await browser.newContext();
    const freshPage = await freshContext.newPage();
    await freshPage.addInitScript(() => {
      localStorage.setItem("eduflow.tenant", "demo");
    });
    await freshPage.goto("/login");
    await expect.poll(async () => {
      return await freshPage.evaluate(() =>
        getComputedStyle(document.documentElement).getPropertyValue("--primary").trim().toLowerCase(),
      );
    }).not.toBe("#1f4e46");
    await expect(freshPage.locator("#institute")).toHaveValue("demo");
    await freshContext.close();
  });

  test("6. tenant isolation UI check across institutes", async ({ page }) => {
    const suffix = Date.now().toString().slice(-6);
    const subdomain = `e2e-iso-${suffix}`;

    await page.goto("/signup");
    await page.locator("#instituteName").fill(`Isolated Institute ${suffix}`);
    await page.locator("#subdomain").fill(subdomain);
    await page.locator("#adminFullName").fill("Malini Fernando");
    await page.locator("#adminEmail").fill(`malini-${suffix}@eduflow.lk`);
    await page.locator("#phone").fill("0773334455");
    await page.locator("#adminPassword").fill("StrongPass@123!");
    await page.locator("#planCode").selectOption("Starter");
    await page.getByRole("button", { name: "Start a 14-day trial" }).click();

    await expect(page).toHaveURL(/\/app$/);
    await page.goto("/app/students");
    await expect(page.getByText(/No students match this filter\./i)).toBeVisible();
    await expect(page.getByText("Nimal Perera")).toHaveCount(0);
    await expect(page.getByText("Amaya Silva")).toHaveCount(0);
  });

  test("7. plan limit reached message shown in UI", async ({ page }) => {
    const suffix = Date.now().toString().slice(-6);
    const subdomain = `e2e-limit-${suffix}`;

    await page.goto("/signup");
    await page.locator("#instituteName").fill(`Starter Limit School ${suffix}`);
    await page.locator("#subdomain").fill(subdomain);
    await page.locator("#adminFullName").fill("Nadeesha Silva");
    await page.locator("#adminEmail").fill(`nadeesha-${suffix}@eduflow.lk`);
    await page.locator("#phone").fill("0776667788");
    await page.locator("#adminPassword").fill("StrongPass@123!");
    await page.locator("#planCode").selectOption("Starter");
    await page.getByRole("button", { name: "Start a 14-day trial" }).click();
    await expect(page).toHaveURL(/\/app$/);

    await page.goto("/app/academics/subjects");
    for (let i = 1; i <= 5; i++) {
      await page.getByRole("button", { name: "New subject" }).click();
      await page.locator("#subject-name").fill(`Subject ${i}`);
      await page.locator("#subject-code").fill(`SUB${i}`);
      await page.getByRole("button", { name: "Save" }).click();
      await expect(page.getByText(`Subject ${i}`)).toBeVisible();
    }

    // 6th subject exceeds Starter plan limit of 5
    await page.getByRole("button", { name: "New subject" }).click();
    await page.locator("#subject-name").fill("Subject 6");
    await page.locator("#subject-code").fill("SUB6");
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByText(/limit/i)).toBeVisible();
  });
});
