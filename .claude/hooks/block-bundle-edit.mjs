// PreToolUse hook: deny Edit/Write on the built editor.bundle.js.
// The bundle is a build artifact — edit QuinSlate.Ui/WebEditor/build/src/main.js instead.
let raw = "";
process.stdin.on("data", (chunk) => (raw += chunk));
process.stdin.on("end", () => {
  let input;
  try {
    input = JSON.parse(raw);
  } catch {
    process.exit(0);
  }
  const filePath = (input.tool_input && input.tool_input.file_path) || "";
  const normalized = filePath.replace(/\\/g, "/").toLowerCase();
  if (normalized.endsWith("/webeditor/editor.bundle.js")) {
    console.log(
      JSON.stringify({
        hookSpecificOutput: {
          hookEventName: "PreToolUse",
          permissionDecision: "deny",
          permissionDecisionReason:
            "editor.bundle.js is a built artifact and must never be hand-edited. " +
            "Edit QuinSlate.Ui/WebEditor/build/src/main.js, then rebuild: " +
            "cd QuinSlate.Ui/WebEditor/build && npm ci && npm run build. " +
            "See Docs/Wiki/06-WEB-EDITOR-BUNDLE.md.",
        },
      }),
    );
  }
  process.exit(0);
});