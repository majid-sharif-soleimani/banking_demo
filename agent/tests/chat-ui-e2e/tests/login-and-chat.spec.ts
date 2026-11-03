import { expect, test, type APIRequestContext } from "@playwright/test";

const gateway = process.env.GATEWAY_URL ?? "http://localhost:5100";

// A fresh AppHost has no customers: register one through the gateway as the admin, as the data import would. The
// national id is the customer's username and password (workspace ADR 0007).
async function registerCustomer(request: APIRequestContext): Promise<{ nationalId: string; name: string }> {
  const login = await request.post(`${gateway}/api/identity/login`, { data: { username: "admin", password: "admin" } });
  expect(login.ok()).toBeTruthy();
  const { token } = (await login.json()) as { token: string };
  const nationalId = `E2E-${Date.now()}`;
  const name = "Erik Testsson";
  const registered = await request.post(`${gateway}/api/customers/customers`, {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      name,
      nationalId,
      dateOfBirth: "1990-01-01",
      email: "e2e@example.com",
      phone: "+46 70 000 00 00",
      address: "Testgatan 1, Stockholm",
      preferredLanguage: "en",
    },
  });
  expect(registered.ok()).toBeTruthy();
  return { nationalId, name };
}

test("a customer logs in, lands in an empty new chat with no trace of the login, chats and logs out", async ({ page, request }) => {
  const customer = await registerCustomer(request);
  await page.goto("/");

  await page.getByLabel("Username").fill(customer.nationalId);
  await page.getByLabel("Password").fill(customer.nationalId);
  await page.getByRole("button", { name: "Log in" }).click();

  // The new chat: a greeting by name, no login fields, and the login values nowhere on the page.
  await expect(page.getByText(`Hello ${customer.name}.`)).toBeVisible();
  await expect(page.getByLabel("Password")).toHaveCount(0);
  expect(await page.content()).not.toContain(customer.nationalId);

  // The session cookie is HttpOnly: scripts on the page cannot read it.
  expect(await page.evaluate(() => document.cookie)).not.toContain("bank_chat_session");

  await page.getByLabel("Message").fill("hello bank");
  await page.getByRole("button", { name: "Send" }).click();
  await expect(page.getByText("You said: hello bank")).toBeVisible();

  await page.getByRole("button", { name: "Log out" }).click();
  await expect(page.getByLabel("Username")).toBeVisible();
  await expect(page.getByText("You logged out.")).toBeVisible();
});

test("a second login of the same user closes the first chat, and the first tab is told why", async ({ browser, request }) => {
  const customer = await registerCustomer(request);
  const first = await (await browser.newContext()).newPage();
  const second = await (await browser.newContext()).newPage();

  for (const page of [first, second]) {
    await page.goto("/");
    await page.getByLabel("Username").fill(customer.nationalId);
    await page.getByLabel("Password").fill(customer.nationalId);
    await page.getByRole("button", { name: "Log in" }).click();
    await expect(page.getByText(`Hello ${customer.name}.`)).toBeVisible();
  }

  await first.getByLabel("Message").fill("are you there?");
  await first.getByRole("button", { name: "Send" }).click();

  await expect(first.getByLabel("Username")).toBeVisible();
  await expect(first.getByRole("status")).toContainText(/somewhere else|newer login/);
});

test("the admin logs in and chats", async ({ page }) => {
  await page.goto("/");
  await page.getByLabel("Username").fill("admin");
  await page.getByLabel("Password").fill("admin");
  await page.getByRole("button", { name: "Log in" }).click();

  await expect(page.getByText("You are logged in as the admin.")).toBeVisible();
  await page.getByLabel("Message").fill("status report");
  await page.getByRole("button", { name: "Send" }).click();
  await expect(page.getByText("You said: status report")).toBeVisible();
});

test("a wrong password keeps the login form and says so", async ({ page }) => {
  await page.goto("/");
  await page.getByLabel("Username").fill("admin");
  await page.getByLabel("Password").fill("wrong");
  await page.getByRole("button", { name: "Log in" }).click();

  await expect(page.getByRole("alert")).toHaveText("Wrong username or password.");
  await expect(page.getByLabel("Password")).toHaveValue("");
});

test("on a phone the message box stays on screen after many messages", async ({ browser }) => {
  const page = await (await browser.newContext({ viewport: { width: 390, height: 664 }, isMobile: true, hasTouch: true })).newPage();
  await page.goto("/");
  await page.getByLabel("Username").fill("admin");
  await page.getByLabel("Password").fill("admin");
  await page.getByRole("button", { name: "Log in" }).click();

  for (let i = 1; i <= 8; i++) {
    await page.getByLabel("Message").fill(`message number ${i} with a little more text so the bubbles take some room`);
    await page.getByRole("button", { name: "Send" }).click();
    await expect(page.getByText(`You said: message number ${i}`)).toBeVisible();
  }

  // The whole page is one screen high: the message box and the send button are inside the visible area.
  await expect(page.getByLabel("Message")).toBeInViewport();
  await expect(page.getByRole("button", { name: "Send" })).toBeInViewport();
  expect(await page.evaluate(() => document.documentElement.scrollHeight <= window.innerHeight + 1)).toBeTruthy();
});
