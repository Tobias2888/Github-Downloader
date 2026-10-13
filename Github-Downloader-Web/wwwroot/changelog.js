'use strict';

window.Changelog = (() => {
    const markdown = new marked.Marked({ gfm: true, breaks: false });
    const allowedTags = [
        'a', 'p', 'br', 'hr', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
        'ul', 'ol', 'li', 'blockquote', 'pre', 'code', 'strong', 'em', 'del',
        'table', 'thead', 'tbody', 'tr', 'th', 'td', 'img', 'input',
        'details', 'summary', 'kbd', 'samp', 'sub', 'sup', 'div', 'span',
    ];

    function safeUrl(value, baseUrl, protocols) {
        try {
            const url = new URL(value, baseUrl);
            return protocols.includes(url.protocol) ? url.href : null;
        } catch {
            return null;
        }
    }

    function render(container, source, repoUrl) {
        if (!source?.trim()) {
            const empty = document.createElement('p');
            empty.className = 'changelog-empty';
            empty.textContent = 'No changelog available for this release.';
            container.replaceChildren(empty);
            return;
        }

        // Release notes are external content. Sanitize before adding them to the page.
        const content = DOMPurify.sanitize(markdown.parse(source), {
            RETURN_DOM_FRAGMENT: true,
            ALLOWED_TAGS: allowedTags,
            ALLOWED_ATTR: ['href', 'src', 'alt', 'title', 'align', 'start', 'type', 'checked', 'disabled', 'open'],
            ALLOW_DATA_ATTR: false,
            ALLOW_ARIA_ATTR: false,
        });
        const baseUrl = safeUrl(`${(repoUrl || '').replace(/\/+$/, '')}/`, undefined, ['https:', 'http:']);

        for (const link of content.querySelectorAll('a[href]')) {
            const href = safeUrl(link.getAttribute('href'), baseUrl, ['https:', 'http:', 'mailto:']);
            if (href) {
                link.href = href;
                link.target = '_blank';
                link.rel = 'noopener noreferrer';
            } else {
                link.removeAttribute('href');
            }
        }

        for (const image of content.querySelectorAll('img')) {
            const src = image.getAttribute('src');
            const href = src && safeUrl(src, baseUrl, ['https:', 'http:']);
            if (href) {
                image.src = href;
                image.loading = 'lazy';
                image.referrerPolicy = 'no-referrer';
            } else {
                image.removeAttribute('src');
            }
        }

        for (const input of content.querySelectorAll('input')) {
            if (input.type !== 'checkbox') input.remove();
            else input.disabled = true;
        }

        // Work on rendered text so existing links, inline code and fenced code stay intact.
        if (baseUrl) {
            const walker = document.createTreeWalker(content, NodeFilter.SHOW_TEXT);
            const nodes = [];
            while (walker.nextNode()) nodes.push(walker.currentNode);
            for (const node of nodes) {
                if (node.parentElement?.closest('a, pre, code, kbd, samp')) continue;
                const matches = [...node.textContent.matchAll(/(?<![\w/\\.#&])#(\d+)\b/g)];
                if (!matches.length) continue;
                const replacement = document.createDocumentFragment();
                let offset = 0;
                for (const match of matches) {
                    replacement.append(node.textContent.slice(offset, match.index));
                    const link = document.createElement('a');
                    link.href = `${baseUrl}issues/${match[1]}`;
                    link.target = '_blank';
                    link.rel = 'noopener noreferrer';
                    link.textContent = match[0];
                    replacement.append(link);
                    offset = match.index + match[0].length;
                }
                replacement.append(node.textContent.slice(offset));
                node.replaceWith(replacement);
            }
        }

        container.replaceChildren(content);
    }

    return { render };
})();
