// links.js: which run of characters in a line is a link, and where it ends. The document text is
// never rewritten by this feature, so every assertion here is about detection alone — what gets the
// accent colour and what the host is asked to open.

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { findLinks } from "../src/links.js";

// The hrefs of every link found in `text`, in order.
function hrefs(text) {
  return findLinks(text).map((link) => link.href);
}

describe("link detection", () => {
  it("finds an https URL on its own line", () => {
    assert.deepEqual(hrefs("https://example.com"), ["https://example.com"]);
  });

  it("finds an http URL", () => {
    assert.deepEqual(hrefs("http://example.com/a/b?c=1&d=2"), ["http://example.com/a/b?c=1&d=2"]);
  });

  it("finds a mailto URL", () => {
    assert.deepEqual(hrefs("mailto:someone@example.com"), ["mailto:someone@example.com"]);
  });

  it("reports the offsets of the link within the line", () => {
    const links = findLinks("see https://example.com now");
    assert.equal(links.length, 1);
    assert.equal(links[0].from, 4);
    assert.equal(links[0].to, 23);
  });

  it("finds several links on one line", () => {
    assert.deepEqual(hrefs("https://a.com and https://b.com"), ["https://a.com", "https://b.com"]);
  });

  it("matches the scheme case-insensitively", () => {
    assert.deepEqual(hrefs("HTTPS://EXAMPLE.COM"), ["HTTPS://EXAMPLE.COM"]);
  });

  it("keeps a port, a fragment, and credentials in the href", () => {
    assert.deepEqual(hrefs("http://localhost:3000/x#top"), ["http://localhost:3000/x#top"]);
  });
});

describe("link non-matches", () => {
  it("ignores a bare domain — 'example.com' is text", () => {
    assert.deepEqual(hrefs("example.com"), []);
  });

  it("ignores filenames that look like bare domains", () => {
    assert.deepEqual(hrefs("node.js README.md main.rs app.config"), []);
  });

  it("ignores a www. host with no scheme", () => {
    assert.deepEqual(hrefs("www.example.com"), []);
  });

  it("ignores a bare email address", () => {
    assert.deepEqual(hrefs("someone@example.com"), []);
  });

  it("ignores an unsupported scheme", () => {
    assert.deepEqual(hrefs("ftp://example.com file:///c:/x javascript:alert(1)"), []);
  });

  it("ignores a scheme with nothing after it", () => {
    assert.deepEqual(hrefs("https:// mailto:"), []);
  });

  it("ignores a scheme reduced to nothing by trailing punctuation", () => {
    assert.deepEqual(hrefs("https://."), []);
  });

  it("declines a scheme glued to the end of a word", () => {
    assert.deepEqual(hrefs("xhttps://example.com"), []);
  });

  it("finds nothing in ordinary prose", () => {
    assert.deepEqual(hrefs("Buy milk, i.e. the 2% one -- 3.50 at most!"), []);
  });
});

describe("link boundaries", () => {
  it("stops at whitespace", () => {
    assert.deepEqual(hrefs("https://example.com and more"), ["https://example.com"]);
  });

  it("drops a sentence-ending period", () => {
    assert.deepEqual(hrefs("Go to https://example.com."), ["https://example.com"]);
  });

  it("drops other trailing sentence punctuation", () => {
    assert.deepEqual(hrefs("https://example.com, https://b.com; https://c.com!"), [
      "https://example.com",
      "https://b.com",
      "https://c.com",
    ]);
  });

  it("drops a trailing paren the URL never opened", () => {
    assert.deepEqual(hrefs("(see https://example.com)"), ["https://example.com"]);
  });

  it("keeps parens that belong to the URL", () => {
    assert.deepEqual(hrefs("https://en.wikipedia.org/wiki/Foo_(bar)"), [
      "https://en.wikipedia.org/wiki/Foo_(bar)",
    ]);
  });

  it("keeps the URL's own parens but drops the wrapping one", () => {
    assert.deepEqual(hrefs("(https://en.wikipedia.org/wiki/Foo_(bar))"), [
      "https://en.wikipedia.org/wiki/Foo_(bar)",
    ]);
  });

  it("drops a trailing angle bracket", () => {
    assert.deepEqual(hrefs("<https://example.com>"), ["https://example.com"]);
  });

  it("keeps a trailing slash", () => {
    assert.deepEqual(hrefs("https://example.com/"), ["https://example.com/"]);
  });

  it("keeps a path that ends in a dot-separated filename", () => {
    assert.deepEqual(hrefs("https://example.com/x/report.pdf"), ["https://example.com/x/report.pdf"]);
  });
});

describe("links while being typed", () => {
  it("matches a URL that is still only a scheme and a host", () => {
    assert.deepEqual(hrefs("https://e"), ["https://e"]);
  });

  it("re-detects after the line grows — detection has no state", () => {
    assert.deepEqual(hrefs("https://example.co"), ["https://example.co"]);
    assert.deepEqual(hrefs("https://example.com"), ["https://example.com"]);
  });
});
