// Generate benchmark.html with Program --benchmark <local document> first.
// Network is blocked: private document references must not be fetched by a benchmark.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const path = require('path');
const fs = require('fs');
const { pathToFileURL } = require('url');
const assert = require('assert');
(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 720, height: 900 } });
        await page.route(/^https?:/, route => route.abort());
        await page.addInitScript(() => {
            window.cacheRequests = [];
            window.external.notify = value => window.cacheRequests.push(value);
        });
        const started = Date.now();
        await page.goto(pathToFileURL(path.join(__dirname, 'artifacts/benchmark.html')).href);
        // Mirror the native NavigationCompleted -> acknowledged patch sequence.
        const patch = fs.readFileSync(path.join(__dirname, 'artifacts/benchmark-patch.js'), 'utf8');
        assert.strictEqual(await page.evaluate(script => window.eval(script), patch), 'ok');
        const result = await page.evaluate(() => ({
            blocks: document.getElementById('content').children.length,
            images: document.querySelectorAll('#content img').length,
            textLength: document.getElementById('content').textContent.length
        }));
        assert(result.blocks > 0 && result.textLength > 0, 'completion patch must display the document without scrolling');
        result.navigationMs = Date.now() - started;
        await page.waitForTimeout(300);
        result.requestedImages = await page.evaluate(() => window.cacheRequests.length);
        if (result.images > 30) assert(result.requestedImages < result.images, 'offscreen images must remain deferred');
        console.log(JSON.stringify(result));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
