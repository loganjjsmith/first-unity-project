---
name: commit-on-claude
description: The git discipline for this project - which branch, what needs Logan's thumbs-up first, how commit messages are written here, and what the agent must never run. Use before committing anything, when asked to save or check in work, and when unsure whether a git command is allowed.
---

# Committing in this project

## Before the commit

1. **Work happens on `claude`** (or a topic branch off it). Confirm with `git rev-parse --abbrev-ref HEAD`.
2. **No commit without Logan's thumbs-up.** Not "it compiles", not "tests pass", not "it's obviously
   fine" - his explicit go-ahead on this change. If the change needs a playtest to judge (anything
   touching feel: movement, camera, physics, launch), the thumbs-up means he has played it.
3. **The code is verified** - see the `verify-csharp` skill. Typecheck at minimum.
4. **Check what is actually staged**: `git status` then `git diff --staged`. Never `git add -A` blindly
   in a Unity repo; generated files and `.meta` churn sneak in that way.

## The commit message

Match the existing log - read the last few with `git log -3` if unsure. The house style:

- Imperative subject line, no type prefix, no ticket id.
- Blank line, then prose paragraphs wrapped at roughly 95 characters.
- The body explains **why**, and records what was deliberately *not* done and the reason. That
  context is the point of the message.
- End with the attribution trailer. Use the exact line the harness supplies in its attribution
  reminder for the current session, not a copy from an older commit. The model name changes with
  upgrades, so a line hardcoded here would go stale:

  ```
  Co-Authored-By: Claude <model name from the harness> <noreply@anthropic.com>
  ```

## After the commit

**Give Logan the hash. He pushes**, from his own terminal. The agent never pushes, for any reason,
and must not even type the command - the PreToolUse guard matches the text anywhere in a shell
command, including inside a heredoc.

## Never, on any branch, for any reason

`main` and `stable` belong to Logan. `stable` is his rollback point - the last state he approved - and
he moves it forward himself from his own terminal.

Forbidden to the agent: pushing, any checkout/switch/reset onto `main` or `stable`, branch delete /
force / rename, history rewrites (`filter-branch`, `filter-repo`, `reflog expire`), `git config`,
remote changes, and state-changing `gh` commands.

Three guard layers enforce this (`permissions.deny` in `.claude/settings.json`, the PreToolUse hook in
`.claude/hooks/`, and the git `reference-transaction` hook in `tools/`), but they all run as Logan's
user on Logan's machine, so they stop accidents rather than intent. The remaining safeguard is that
only Logan ever pushes. If something genuinely needs to happen on a protected branch, ask him to run it.
