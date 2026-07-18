const { chromium } = require('playwright');
const fs = require('fs');

(async () => {
    console.log("Launching browser...");
    const browser = await chromium.launch({ headless: true });
    const page = await browser.newPage();

    console.log("Navigating to http://localhost:5800...");
    await page.goto("http://localhost:5800");

    console.log("Clicking 'Enable accessibility'...");
    await page.dispatchEvent('flt-semantics-placeholder', 'click');

    console.log("Waiting 5 seconds for Flutter to mount the accessibility tree...");
    await page.waitForTimeout(5000);

    console.log("Taking accessibility snapshot...");
    try {
        if (page.accessibility) {
            const ax = await page.accessibility.snapshot();
            fs.writeFileSync("C:/Users/vhorb/.gemini/antigravity/scratch/ax_snapshot.json", JSON.stringify(ax, null, 2));
        } else {
            console.log("page.accessibility is not supported in this Playwright version.");
        }
    } catch (e) {
        console.log("Failed to take accessibility snapshot:", e.message);
    }

    console.log("Taking page HTML dump...");
    const html = await page.content();
    fs.writeFileSync("C:/Users/vhorb/.gemini/antigravity/scratch/page_html.html", html);

    console.log("Taking screenshot...");
    await page.screenshot({ path: "C:/Users/vhorb/.gemini/antigravity/scratch/page_loaded.png" });

    await browser.close();
    console.log("Done dumping DOM and accessibility tree!");
})();
