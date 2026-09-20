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
        assert.strictEqual(await page.locator('#content > .md-block').count(), 0,
            'navigation installs helpers; native completion owns the first content patch');
        const patches = JSON.parse(fs.readFileSync(path.join(artifacts, 'patches.json'), 'utf8'));
        assert.strictEqual(await page.evaluate(script => window.eval(script), patches[0]), 'ok');

        const imageRetry = await browser.newPage();
        await imageRetry.goto(pathToFileURL(path.join(artifacts, 'preview.html')).href);
        assert(await imageRetry.evaluate(() => {
            const host = document.getElementById('content');
            host.innerHTML = '<div class="md-block"><img data-src="https://example.test/retry.png"></div>';
            const img = host.querySelector('img');
            let requests = 0;
            window.external.notify = () => { requests++; };
            window.__mdProcessVisibleImages(host);
            const near = window.__mdIsNearViewport;
            window.__mdIsNearViewport = () => false;
            window.__mdImageReady('https://example.test/retry.png', '/image/retry.png');
            const released = !img.__mdActive && !img.__mdCachePending;
            window.__mdIsNearViewport = near;
            window.__mdProcessVisibleImages(host);
            return released && requests === 2;
        }), 'a skipped image reply must allow activation again without leaving the release margin');
        await imageRetry.close();

        const cacheBridge = await page.evaluate(() => {
            const notices = [];
            window.external.notify = message => notices.push(message);
            const url = 'https://example.test/cached-image.png';
            const data = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==';
            const host = document.getElementById('content');
            const images = [document.createElement('img'), document.createElement('img')];
            images.forEach(img => {
                img.setAttribute('data-src', url);
                img.setAttribute('data-placeholder-src', data);
                img.src = data;
                host.prepend(img);
                window.__mdActivateImage(img);
            });
            const coalesced = notices.length === 1;
            window.__mdImageReady(url, data);
            const applied = images.every(img => img.src === data && img.getAttribute('data-original-src') === url);
            // Use a distinct placeholder to exercise releasing the display source.
            images[0].setAttribute('data-placeholder-src', 'data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs=');
            window.__mdReleaseFarImage(images[0]);
            window.__mdActivateImage(images[0]);
            const reloadUsesOriginalUrl = notices.length === 2 && notices[1] === 'md-image-cache:' + url;
            window.__mdImageReady(url, data);
            images.forEach(img => img.remove());
            delete window.external.notify;
            return { coalesced, applied, reloadUsesOriginalUrl };
        });
        assert.deepStrictEqual(cacheBridge, { coalesced: true, applied: true, reloadUsesOriginalUrl: true });
        const duringScroll = await page.evaluate(() => {
            const host = document.getElementById('content');
            const block = document.createElement('div'); block.className = 'md-block';
            const img = document.createElement('img');
            window.scrollTestSource = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==';
            img.src = 'data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs=';
            img.setAttribute('data-src', 'https://example.test/idle.png');
            block.appendChild(img); host.prepend(block);
            window.scrollTestBlock = block;
            window.savedHeavy = window.__mdProcessBlockHeavy;
            window.scrollHeavyCalls = 0;
            window.__mdProcessBlockHeavy = b => { window.scrollHeavyCalls++; window.savedHeavy(b); };
            window.external.notify = () => {};
            window.__mdActivateImage(img);
            window.dispatchEvent(new Event('scroll'));
            window.__mdImageReady('https://example.test/idle.png', window.scrollTestSource);
            window.__mdProcessVisibleBlocks(host, 2);
            return { deferredImage: img.src !== window.scrollTestSource, heavyCalls: window.scrollHeavyCalls };
        });
        assert.deepStrictEqual(duringScroll, { deferredImage: true, heavyCalls: 0 });
        await page.waitForTimeout(350);
        assert(await page.evaluate(() => {
            const restored = window.scrollTestBlock.firstChild.src === window.scrollTestSource && window.scrollHeavyCalls > 0;
            window.__mdProcessBlockHeavy = window.savedHeavy;
            window.scrollTestBlock.remove(); delete window.external.notify;
            return restored;
        }), 'deferred image and heavy work must resume after scrolling stops');
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
        await page.evaluate(() => {
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
        });
        await page.waitForTimeout(300);
        const geometry = await page.evaluate(() => {
            window.geometryReads = 0;
            window.__mdProcessVisibleBlocks(document.getElementById('content'), 2);
            return window.geometryReads;
        });
        assert(geometry > 0, 'idle pass must actually inspect visible blocks');
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
