// Run after dotnet run. PLAYWRIGHT_MODULE may point to an existing bundled runtime.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');
const assert = require('assert');

(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage({ viewport: { width: 1100, height: 760 } });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
        const artifacts = path.resolve(__dirname, 'artifacts');
        await page.goto(pathToFileURL(path.join(artifacts, 'preview.html')).href);
        const patches = JSON.parse(fs.readFileSync(path.join(artifacts, 'patches.json'), 'utf8'));
        assert.strictEqual(await page.evaluate(script => window.eval(script), patches[0]), 'ok');
        assert.strictEqual(await page.locator('#content h1').innerText(), '标题');
        await page.evaluate(() => { window.originalNodes = Array.prototype.slice.call(document.getElementById('content').children); });
        assert.strictEqual(await page.evaluate(script => window.eval(script), patches[1]), 'ok');
        assert.strictEqual(await page.locator('#content > div').count(), 4);
        assert(await page.evaluate(() => window.originalNodes.every((node, i) => node === document.getElementById('content').children[i + 1])));
        assert.strictEqual(await page.evaluate(script => window.eval(script), patches[2]), 'ok');
        assert(await page.evaluate(() => window.originalNodes.every((node, i) => node === document.getElementById('content').children[i])));
        assert.strictEqual(await page.evaluate(() => window.__mdApplyPatches([], 999, [], false, 0)), 'mismatch');
        assert.strictEqual(await page.locator('#content > div').count(), 3);
        assert.strictEqual(await page.evaluate(() => window.__mdApplyPatches([], 0, [], true, 0)), 'ok');
        assert.strictEqual(await page.locator('#content > div').count(), 0);
        assert.strictEqual(await page.evaluate(script => window.eval(script), patches[0]), 'ok');

        // Exercise actual scroll/layout, not only string comparisons.
        const geometry = await page.evaluate(() => {
            const host = document.getElementById('content');
            for (let i = 0; i < 10000; i++) {
                const block = document.createElement('div');
                block.className = 'md-block'; block.textContent = 'Paragraph ' + i;
                host.appendChild(block);
            }
            window.geometryReads = 0;
            for (let i = 0; i < host.children.length; i++) {
                const node = host.children[i], read = node.getBoundingClientRect.bind(node);
                node.getBoundingClientRect = function () { window.geometryReads++; return read(); };
            }
            window.scrollTo(0, document.body.scrollHeight / 2);
            window.__mdProcessVisibleBlocks(host, 2);
            return window.geometryReads;
        });
        assert(geometry < 150, 'viewport lookup must not inspect all 10000 blocks: ' + geometry);
        await page.screenshot({ path: path.join(artifacts, 'preview-desktop.png') });
        await page.setViewportSize({ width: 390, height: 760 });
        await page.screenshot({ path: path.join(artifacts, 'preview-mobile.png') });
        await page.goto(pathToFileURL(path.join(artifacts, 'rich-preview.html')).href);
        const richPatch = JSON.parse(fs.readFileSync(path.join(artifacts, 'rich-patch.json'), 'utf8'));
        assert.strictEqual(await page.evaluate(script => window.eval(script), richPatch), 'ok');
        await page.locator('.hljs').first().waitFor();
        await page.locator('.katex').first().waitFor();
        await page.locator('.mermaid svg').first().waitFor();
        await page.screenshot({ path: path.join(artifacts, 'preview-rich.png') });
        assert.deepStrictEqual(errors, []);
        console.log(JSON.stringify({ result: 'PASS', url: page.url(), title: await page.title(), preservedNodes: true, geometryReadsFor10000Blocks: geometry, consoleErrors: errors, viewports: ['1100x760', '390x760'], limitation: 'Edge test harness, not Windows 8.1 WebView or native RichEditBox' }, null, 2));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
