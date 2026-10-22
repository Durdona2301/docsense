import { test, expect } from "@playwright/test";

test("documents, citations, reader, validation and responsive layouts", async ({
  page,
  request,
}) => {
  // Run against a disposable local/CI database. Only our sample and test documents are touched.
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  await page.setViewportSize({ width: 1440, height: 960 });
  await page.goto("/");
  await expect(page.getByText("No AI model connected")).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Ask question", exact: true }),
  ).toBeDisabled();
  await page
    .getByRole("button", { name: "Load sample library", exact: true })
    .click();
  await expect(
    page.getByRole("button", { name: "Read Product handbook.md", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Ask question", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Relevant passage" }),
  ).toBeVisible();
  await expect(page.locator("#answer-text")).toContainText("14-day trial");
  await expect(page.locator("#sources .source")).not.toHaveCount(0);
  await expect(page.locator("#answer-notice")).toContainText(
    "No AI-generated answer",
  );
  await page.getByRole("button", { name: /Inspect source 1:/ }).click();
  await expect(page.locator("#source-1 details")).toHaveAttribute("open", "");
  await page.evaluate(() => scrollTo(0, 0));
  await page.screenshot({
    path: "artifacts/screenshots/desktop.png",
    fullPage: true,
  });
  await page.setViewportSize({ width: 1536, height: 1024 });
  await page.evaluate(() => scrollTo(0, 0));
  await page.screenshot({ path: ".impeccable/review/hero-repro.png" });
  await page.setViewportSize({ width: 390, height: 844 });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  await page.evaluate(() => scrollTo(0, 0));
  await page.screenshot({
    path: "artifacts/screenshots/mobile.png",
    fullPage: true,
  });
  await page.setViewportSize({ width: 1440, height: 960 });
  await page
    .getByRole("button", { name: "Read Product handbook.md", exact: true })
    .click();
  await expect(
    page.getByRole("heading", { name: "Product handbook.md", exact: true }),
  ).toBeVisible();
  await expect(page.locator("#reader-content")).toContainText("14-day trial");
  await page.getByRole("button", { name: "Back to questions" }).click();
  const docs = await (await request.get("/api/documents")).json();
  const apiDoc = docs.find(
    (doc: { name: string }) => doc.name === "API reference.txt",
  );
  await page.getByLabel("Search in document").selectOption(apiDoc.id);
  await page
    .getByRole("textbox", { name: "Your question" })
    .fill("Elephant migration in Antarctica?");
  await page
    .getByRole("textbox", { name: "Your question" })
    .press("Control+Enter");
  await expect(
    page.getByRole("heading", { name: "More evidence needed" }),
  ).toBeVisible();
  await expect(page.locator("#sources-section")).toBeHidden();
  await page.screenshot({
    path: "artifacts/screenshots/no-evidence.png",
    fullPage: true,
  });
  await page.locator("#file-input").setInputFiles({
    name: "bad.exe",
    mimeType: "application/octet-stream",
    buffer: Buffer.from("bad"),
  });
  await expect(page.getByRole("alert")).toContainText("Choose a PDF");
  await page.getByRole("button", { name: "Dismiss message" }).click();
  const filename = `browser-${Date.now()}.md`;
  await page.locator("#file-input").setInputFiles({
    name: filename,
    mimeType: "text/markdown",
    buffer: Buffer.from(
      "The sapphire support window is 24 hours. <img src=x onerror=alert(1)>",
    ),
  });
  await expect(
    page.getByRole("button", { name: `Read ${filename}`, exact: true }),
  ).toBeVisible();
  await page
    .getByRole("button", { name: `Read ${filename}`, exact: true })
    .click();
  await expect(page.locator("#reader-content")).toContainText("<img src=x");
  await expect(page.locator("#reader-content img")).toHaveCount(0);
  await page
    .getByRole("button", { name: `Remove ${filename}`, exact: true })
    .click();
  await page.getByRole("button", { name: "Remove", exact: true }).click();
  await expect(
    page.getByRole("button", { name: `Read ${filename}`, exact: true }),
  ).toHaveCount(0);
  expect(errors).toEqual([]);
});

test("failed library request offers recovery", async ({ page }) => {
  await page.route("**/api/documents", (route) =>
    route.fulfill({
      status: 503,
      contentType: "application/problem+json",
      body: JSON.stringify({
        detail: "Database temporarily unavailable. Retry connection.",
      }),
    }),
  );
  await page.goto("/");
  await expect(page.getByRole("alert")).toContainText(
    "Database temporarily unavailable",
  );
  await expect(
    page.getByRole("button", { name: "Retry connection" }),
  ).toBeVisible();
  await page.unroute("**/api/documents");
  await page.getByRole("button", { name: "Retry connection" }).click();
  await expect(page.locator("#mode-label")).toHaveText("Extractive mode");
  await expect(
    page.getByRole("button", { name: "Load sample library", exact: true }),
  ).toBeVisible();
});
