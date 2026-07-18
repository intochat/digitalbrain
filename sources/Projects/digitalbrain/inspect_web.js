const { chromium } = require('playwright');
const fs = require('fs');

(async () => {
  console.log('Launching browser...');
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext();
  const page = await context.newPage();

  const consoleLogs = [];
  page.on('console', msg => {
    const text = `[Console ${msg.type()}] ${msg.text()}`;
    console.log(text);
    consoleLogs.push(text);
  });

  page.on('pageerror', err => {
    const text = `[Page Error] ${err.message}\nStack: ${err.stack}`;
    console.log(text);
    consoleLogs.push(text);
  });

  try {
    console.log('Navigating to http://localhost:5800/ ...');
    await page.goto('http://localhost:5800/', { waitUntil: 'domcontentloaded', timeout: 30000 });
    console.log('Page loaded. Waiting 12 seconds for CanvasKit / HTML rendering...');
    await page.waitForTimeout(12000);

    // Take screenshot to verify visual state
    console.log('Taking screenshot...');
    await page.screenshot({ path: 'C:/Users/vhorb/.gemini/antigravity/brain/124325f8-759e-4968-9bdf-0aa4abbd55a4/screenshot_verification_final.png', fullPage: true });

    // Save logs
    fs.writeFileSync('C:/Users/vhorb/.gemini/antigravity/brain/124325f8-759e-4968-9bdf-0aa4abbd55a4/console_logs.txt', consoleLogs.join('\n'));
    console.log('Done!');
  } catch (err) {
    console.error('Error during execution:', err);
    fs.writeFileSync('C:/Users/vhorb/.gemini/antigravity/brain/124325f8-759e-4968-9bdf-0aa4abbd55a4/console_logs.txt', consoleLogs.join('\n') + '\n\nERROR:\n' + err.toString());
  } finally {
    await browser.close();
  }
})();
