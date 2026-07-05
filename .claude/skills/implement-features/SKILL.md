---
name: implement-features
description: Sequentially implement features from 00-FEATURE-QUEUE.md using subagents. Use when the user requests specific feature numbers to implement (e.g. "implement features 10 and 13", "/implement-features 10 13"). Reads each feature's spec file and dispatches a dedicated subagent per feature, updating the queue when each one completes.
---

# Feature Queue Sequential Implementation

You are an Orchestrator. Implement the features the user requested by number, processing them **strictly one at a time** — never start the next feature until the current one is complete and marked done in `Specs/00-FEATURE-QUEUE.md`.

## Setup

1. Parse the feature numbers from the user's invocation arguments or message (e.g. "10 13" → [10, 13]).
2. Read `Specs/00-FEATURE-QUEUE.md` to locate each feature's name and spec file path.
3. Validate every requested number exists in the queue. If any are already marked `[x]`, skip them and tell the user.
4. For each remaining feature number, in the order the user listed them, execute the loop below.

## Per-Feature Loop

### Step 1 — Read the spec

Read the spec file for this feature (the path is listed in 00-FEATURE-QUEUE.md, e.g. `Specs/10-CAPTURE.md`). Also read `CLAUDE.md` for architecture rules and constraints.

### Step 2 — Classify the feature

Before dispatching, read the spec and determine whether the feature touches any UI layer: XAML files, WinUI 3 controls, visual layout, animations, or any `.xaml` / `.xaml.cs` files. Mark it as **UI** or **Non-UI**.

### Step 3 — Dispatch an implementer subagent

- **UI features** — use `subagent_type: "WinUI 3 Expert"`. This is mandatory for anything involving XAML, WinUI 3 controls, visual layout, or animations.
- **Non-UI features** — use `subagent_type: "general-purpose"`.

Call the `Agent` tool with the chosen type and a self-contained prompt that includes **all** of the following — the subagent has no session context:

```
You are implementing feature N: <feature name> for the QuinSlate WinUI 3 application.

## Project context (from CLAUDE.md)
<paste the full CLAUDE.md content here>

## Spec (from Specs/NN-NAME.md)
<paste the full spec file content here>

## Your task
Implement everything described in the spec above. Follow all architecture rules from CLAUDE.md exactly:
- All P/Invoke signatures go in NativeMethods.cs only
- File-scoped namespaces, one class per file, filename = class name
- No nullable reference types, no `?` annotations, no `!` operators
- No third-party NuGet packages
- No regions, no magic numbers
- XML doc comments on all public members in Interop/ and Services/
- Run `dotnet build QuinSlate.slnx -p:Platform=x64` to verify compilation
- Run `dotnet test QuinSlate.slnx -p:Platform=x64` to verify tests pass
- Run `dotnet format QuinSlate.slnx` after every .cs file change

When done, report:
STATUS: DONE
SUMMARY: <one paragraph of what was implemented and any important decisions>

If you cannot complete the task, report:
STATUS: BLOCKED
REASON: <specific blocker>
```

**Wait** for the subagent to finish before proceeding.

### Step 4 — Handle subagent status

**DONE:** Proceed to Step 5.

**BLOCKED:** Report the blocker to the user, stop the entire queue, and wait for guidance. Do not continue to the next feature.

### Step 5 — Verify build and tests pass

Dispatch a second subagent (or run directly) to confirm:
```
dotnet build QuinSlate.slnx -p:Platform=x64
dotnet test QuinSlate.slnx -p:Platform=x64
```

If either fails, dispatch a fix subagent with the error output and the original spec. Do not mark the feature done until both pass.

### Step 6 — Mark feature complete

Edit `Specs/00-FEATURE-QUEUE.md`: change `- [ ] NN` to `- [x] NN` for the completed feature. Do this immediately after the feature passes — before starting the next one.

Tell the user: "Feature N complete: <feature name>."

### Step 7 — Next feature

Move to the next requested feature number and repeat from Step 1.

## Rules

- **Sequential only.** Never dispatch two implementer subagents at the same time.
- **WinUI 3 Expert is mandatory** for any feature touching XAML, WinUI 3 controls, visual layout, or animations. Never use `general-purpose` for UI work.
- **Self-contained prompts.** Every subagent prompt must include the full spec and full CLAUDE.md — never ask subagents to read files themselves.
- **Stop on BLOCKED.** A single blocked feature halts the entire queue. Report clearly and wait.
- **Always verify build+tests** before marking done.
- **Always update 00-FEATURE-QUEUE.md** immediately after each feature passes.
- **Do not commit.** The user will commit when ready.
- If the user gave no feature numbers, ask: "Which feature numbers should I implement?"
