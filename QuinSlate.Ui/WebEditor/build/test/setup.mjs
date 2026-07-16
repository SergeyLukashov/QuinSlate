// Preloaded before every test file (see the "test" script in package.json). The editor modules are
// written for the WebView2 page and read these globals at import time — hostBridge.js reaches for
// window.chrome.webview as its module body runs — so they must exist before any src module is
// imported. A stub is enough: the tests exercise logic, never a real page.
//
// window.chrome is null here, so postToHost/onHostMessage become no-ops and nothing under test
// tries to reach a host that is not there.

globalThis.window = { chrome: null };
globalThis.document = { documentElement: { style: {} } };
