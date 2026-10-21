---
name: verify-csharp
description: Verify this project's C# compiles and its tests pass, with or without the Unity Editor open. Use before reporting code work as done, before asking Logan to playtest, and before any commit that touches a .cs file. Covers the offline typecheck script, the EditMode test suite, and how to read a clean console.
---

# Verifying C#

Two independent checks. Run the typecheck always; run the tests whenever mechanics logic changed.
Never report code work as finished, and never ask Logan to playtest, on unverified code.

## 1. Typecheck (no Editor needed, ~2 s)

```sh
tools/typecheck
```

Run it from the repo root. It points `dotnet build` at the `.csproj` files Unity already generates,
so it uses the same `netstandard2.1` target, reference assemblies and `#define`s the Editor uses.
No Editor, no domain reload, no network. This is the only compile feedback available when Unity is
closed or when working remotely.

Expected output: one line per assembly — `Assembly-CSharp`, `MovementSandbox.Mechanics` and
`MovementSandbox.Mechanics.EditModeTests` among them — each `ok`.

Two failure modes that are **not** code errors:

- **`skipped (stale project file, lists missing sources)`** — a generated `.csproj` outliving files
  that were deleted. Harmless, and `*.csproj` is gitignored.
- **A new script is invisible.** Unity regenerates the `.csproj` files, so a file added while Unity
  was closed does not exist as far as this script is concerned until Unity next opens.

What it cannot catch: missing `.meta` files, broken scene or prefab references, serialized fields
that lost their values, and anything else that only appears at runtime. A clean typecheck means the
code compiles — not that the game works.

## 2. EditMode tests

The suite lives in `MovementSandbox.Mechanics.EditModeTests` and covers the pure mechanics cores
(slingshot maths, mode rules, lockouts, damping, ragdoll recovery) — the logic deliberately split out
of MonoBehaviours so it can be tested without Play mode.

Two ways to run them:

1. Unity's Test Runner window (**Window → General → Test Runner**, EditMode tab), if the Editor is open.
2. Headless, with the Editor closed:
   `Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults results.xml`

If neither was possible, say so plainly rather than implying the suite passed.

## 3. Console

After Editor-side work, check the Console. The baseline is **0 errors** and no unexplained warnings,
so anything new is something the current work introduced rather than pre-existing noise.

One known exception: a full EditMode run legitimately ends with **1 warning**. A regression test
deliberately installs a bad stretch curve to pin the validation, and `LogAssert.Expect` claims the
warning for the test without removing it from the console stream. That warning is expected, not a
project problem.

## Reporting

State what actually ran. "Typecheck clean; tests not run, Unity is closed" is a good report.
"Verified" on its own is not.
