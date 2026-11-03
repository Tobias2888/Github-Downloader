These browser libraries are served locally so changelog rendering does not require a CDN or a JavaScript build step.

- Marked 18.1.1: `marked/lib/marked.umd.js` from the npm `marked@18.1.1` package. Source: https://github.com/markedjs/marked. License: MIT (see `marked/LICENSE`).
- DOMPurify 3.4.17: `dompurify/dist/purify.min.js` from the npm `dompurify@3.4.17` package. Source: https://github.com/cure53/DOMPurify. License: Apache-2.0 or MPL-2.0 (see `dompurify/LICENSE` and `dompurify/LICENSE-MPL`).

To update, download the desired pinned npm packages and replace these distribution files and their licenses together. `changelog.js` sanitizes the parsed Markdown before inserting it into the document.
