import path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "@playwright/test";

const dir = path.dirname(fileURLToPath(import.meta.url));

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
  "/unknown-route-404",
];

test.describe("Public marketing site smoke, responsiveness & accessibility", () => {
  test("every marketing & auth route renders 1 h1, title, and zero console errors", async ({ page }) => {
    const errors: string[] = [];
    page.on("pageerror", (err) => errors.push(err.message));
    page.on("console", (msg) => {
      if (msg.type() === "error") {
        const text = msg.text();
        // Anonymous initial visit calls POST /api/auth/refresh which returns 401 when no cookie is present
        if (text.includes("status of 401 (Unauthorized)")) return;
        errors.push(text);
      }
    });

    await page.setViewportSize({ width: 1440, height: 900 });
    for (const route of routes) {
      await page.goto(route);
      await expect(page.locator("h1")).toHaveCount(1);
      await expect(page.locator("h1")).toBeVisible();
      const title = await page.title();
      expect(title.trim().length, `Route ${route} should have a title`).toBeGreaterThan(0);

      if (route === "/") {
        await page.screenshot({
          path: path.resolve(dir, "../../docs/screenshots/landing.png"),
          fullPage: false,
        });
      }
    }

    expect(errors).toEqual([]);
  });

  test("no horizontal overflow at 360px, 768px, and 1440px and drawer + keyboard navigation work", async ({ page }) => {
    for (const width of [360, 768, 1440]) {
      await page.setViewportSize({ width, height: 800 });
      for (const route of ["/", "/features/attendance", "/pricing", "/demo"]) {
        await page.goto(route);
        await expect(page.locator("h1")).toBeVisible();
        const overflow = await page.evaluate(
          () => document.documentElement.scrollWidth > window.innerWidth,
        );
        expect(overflow, `Horizontal scroll on ${route} at ${width}px`).toBe(false);
      }
    }

    // Mobile drawer menu test at 360px
    await page.setViewportSize({ width: 360, height: 800 });
    await page.goto("/");
    await page.getByRole("button", { name: "Open menu" }).click();
    const drawer = page.getByRole("dialog");
    await expect(drawer).toBeVisible();
    await drawer.getByRole("link", { name: "Pricing" }).click();
    await expect(page).toHaveURL(/\/pricing$/);

    // Desktop keyboard navigation on Features dropdown and Pricing FAQ accordion
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto("/pricing");
    const featuresTrigger = page.getByRole("button", { name: "Features" });
    await featuresTrigger.focus();
    await page.keyboard.press("ArrowDown");
    await expect(featuresTrigger).toHaveAttribute("aria-expanded", "true");
    await page.keyboard.press("Escape");
    await expect(featuresTrigger).toHaveAttribute("aria-expanded", "false");

    const faqBtn = page.getByRole("button", { name: /What happens when SMS quota runs out/i });
    await faqBtn.focus();
    await page.keyboard.press("Enter");
    await expect(faqBtn).toHaveAttribute("aria-expanded", "true");
  });
});
