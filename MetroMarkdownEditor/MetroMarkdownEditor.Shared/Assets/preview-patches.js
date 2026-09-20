(function () {
    'use strict';
    function nearBlocks(host, margin) {
        if (!host) return [];
        if (host.className && host.className.indexOf('md-block') !== -1) return [host];
        var blocks = host.children, low = 0, high = blocks.length, result = [];
        while (low < high) {
            var mid = (low + high) >> 1;
            if (blocks[mid].getBoundingClientRect().bottom < -margin) low = mid + 1;
            else high = mid;
        }
        for (var i = low; i < blocks.length; i++) {
            if (blocks[i].getBoundingClientRect().top > window.innerHeight + margin) break;
            result.push(blocks[i]);
        }
        return result;
    }
    // Query only nearby blocks instead of enumerating the entire DOM on every scroll.
    window.__mdProcessVisibleBlocks = function (root, budget) {
        var blocks = nearBlocks(root || document.getElementById('content'), Math.max(window.innerHeight, 600));
        var count = 0, start = Date.now();
        for (var i = 0; i < blocks.length; i++) {
            if (count >= budget || (count > 0 && Date.now() - start >= 8)) break;
            if (blocks[i].getAttribute('data-heavy-ready') === 'true') continue;
            window.__mdProcessBlockHeavy(blocks[i]);
            count++;
        }
        return count;
    };
    var activeImages = [];
    window.__mdProcessVisibleImages = function (root) {
        var releaseMargin = Math.max(window.innerHeight * 3, 1800), retained = [], i;
        for (i = 0; i < activeImages.length; i++) {
            var image = activeImages[i];
            if (!document.documentElement.contains(image)) { image.__mdActive = false; continue; }
            var rect = image.getBoundingClientRect();
            if (rect.bottom < -releaseMargin || rect.top > window.innerHeight + releaseMargin) {
                window.__mdReleaseFarImage(image);
                image.__mdActive = false;
            } else retained.push(image);
        }
        activeImages = retained;
        var blocks = nearBlocks(root || document.getElementById('content'), Math.max(window.innerHeight, 600));
        for (i = 0; i < blocks.length; i++) {
            var images = blocks[i].getElementsByTagName('img');
            for (var j = 0; j < images.length; j++) {
                var img = images[j];
                if (!img.__mdActive && window.__mdIsNearViewport(img)) {
                    window.__mdActivateImage(img);
                    img.__mdActive = true;
                    activeImages.push(img);
                }
            }
        }
    };
    // ES5 for the Windows 8.1 / Phone 8.1 WebView.
    window.__mdApplyPatches = function (patches, expectedCount, metadata, reset, nextCount) {
        var host = document.getElementById('content');
        if (!host || (!reset && host.children.length !== expectedCount)) return 'mismatch';
        var prepared = [], i, j, patch, temp;
        for (i = 0; i < patches.length; i++) {
            patch = patches[i];
            temp = document.createElement('div');
            temp.innerHTML = decodeURIComponent(escape(window.atob(patch[3])));
            if (temp.children.length !== patch[2]) return 'mismatch';
            for (j = 0; j < temp.children.length; j++) window.__mdPrepareFragment(temp.children[j]);
            prepared.push(temp);
        }
        var doc = document.documentElement, body = document.body;
        var scrollTop = (doc && doc.scrollTop) || (body && body.scrollTop) || 0;
        var anchor = null, anchorTop = 0;
        var low = 0, high = host.children.length;
        while (low < high) {
            var mid = (low + high) >> 1;
            if (host.children[mid].getBoundingClientRect().bottom < 0) low = mid + 1;
            else high = mid;
        }
        if (low < host.children.length) {
            anchor = host.children[low];
            anchorTop = anchor.getBoundingClientRect().top;
        }
        if (reset) host.innerHTML = '';
        // Splices use the old indices. Apply backwards to preserve them.
        for (i = patches.length - 1; i >= 0; i--) {
            patch = patches[i];
            for (j = 0; j < patch[1]; j++) host.removeChild(host.children[patch[0]]);
            var before = host.children[patch[0]] || null;
            temp = prepared[i];
            var fragment = document.createDocumentFragment();
            while (temp.firstChild) fragment.appendChild(temp.firstChild);
            host.insertBefore(fragment, before);
        }
        if (host.children.length !== nextCount) return 'mismatch';
        for (i = 0; i < metadata.length; i++) {
            var block = host.children[metadata[i][0]];
            var attrs = ['data-block', 'data-start', 'data-end'];
            var values = metadata[i];
            for (j = 0; j < attrs.length; j++) {
                if (block.getAttribute(attrs[j]) !== String(values[j])) block.setAttribute(attrs[j], values[j]);
            }
        }
        if (anchor && host.contains(anchor)) scrollTop += anchor.getBoundingClientRect().top - anchorTop;
        if (doc) doc.scrollTop = scrollTop;
        if (body) body.scrollTop = scrollTop;
        if (window.__mdScheduleVisibleBlockPass) window.__mdScheduleVisibleBlockPass(host);
        return 'ok';
    };
}());
