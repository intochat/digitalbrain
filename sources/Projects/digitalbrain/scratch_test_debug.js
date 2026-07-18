const { chromium } = require('playwright');

(async () => {
    console.log("Launching browser...");
    const browser = await chromium.launch({ headless: true });
    const page = await browser.newPage();

    console.log("Navigating to http://localhost:5800...");
    await page.goto("http://localhost:5800");

    console.log("Waiting 5 seconds for page loading...");
    await page.waitForTimeout(5000);

    console.log("Dumping document body HTML...");
    const bodyHtml = await page.evaluate(() => document.body.innerHTML);
    console.log("Body HTML:", bodyHtml);

    await browser.close();
    console.log("Debug script finished!");
})();
