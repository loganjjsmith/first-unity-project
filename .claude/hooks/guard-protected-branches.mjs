#!/usr/bin/env node
// PreToolUse guard: blocks the agent from moving protected branches, pushing,
// rewriting history, or changing GitHub/repo settings.
//
// Logan owns `main` and `stable` and is the only one who pushes. This catches
// accidents and runaway behaviour; it is not a security boundary (see
// AI-WORKFLOW.md). The final safeguard is that only Logan pushes.
//
// Exit 0 = allow. Exit 2 = block, with the reason on stderr.

const PROTECTED = ["main", "stable"];

let raw = "";
for await (const chunk of process.stdin) raw += chunk;

let cmd = "";
try {
  cmd = JSON.parse(raw)?.tool_input?.command ?? "";
} catch {
  process.exit(0); // unparseable payload: don't block real work
}

// Split on shell separators so `foo && git push` is still inspected.
const parts = cmd.split(/(?:&&|\|\||;|\||\n)/).map((s) => s.trim()).filter(Boolean);
const protectedAlt = PROTECTED.join("|");

const RULES = [
  [/\bgit\s+push\b/, "git push — only Logan pushes"],
  [/\bgit\s+branch\b[^\n]*\s-(?:[a-zA-Z]*[DdfMm])\b/, "git branch delete/force/rename"],
  [new RegExp(`\\bgit\\s+(?:checkout|switch)\\b[^\\n]*\\b(?:${protectedAlt})\\b`), "switching to a protected branch"],
  [/\bgit\s+checkout\s+-B\b/, "git checkout -B (force-moves a branch)"],
  [new RegExp(`\\bgit\\s+(?:merge|rebase|reset|cherry-pick)\\b[^\\n]*\\b(?:${protectedAlt})\\b`), "rewriting a protected branch"],
  [/\bgit\s+reset\s+--hard\b/, "git reset --hard (discards committed/working state)"],
  [/\bgit\s+update-ref\b/, "git update-ref (writes refs directly)"],
  [/\bgit\s+(?:filter-branch|filter-repo)\b/, "history rewrite"],
  [/\bgit\s+reflog\s+expire\b/, "reflog expiry (destroys the recovery log)"],
  [/\bgit\s+gc\b[^\n]*--prune/, "aggressive gc prune"],
  [/\bgit\s+tag\b[^\n]*\s-(?:d|f)\b/, "tag delete/force"],
  [/\bgit\s+config\b/, "git config changes (never change git config)"],
  [/\bgit\s+remote\s+(?:set-url|remove|rm|add)\b/, "changing git remotes"],
  [/\bgit\s+clean\b[^\n]*-[a-zA-Z]*f/, "git clean -f (deletes untracked work)"],
  [/\bgh\s+(?:repo|api|auth|release|secret|ssh-key)\b/, "gh command that can change GitHub state or settings"],
  [/\bgh\s+pr\s+(?:merge|close)\b/, "merging/closing a PR"],
  [/\.git[\/\\]hooks/, "touching .git/hooks (the local branch guard lives there)"],
];

for (const part of parts) {
  // Ignore obvious read-only inspections of protected branches.
  if (/\bgit\s+(?:log|show|diff|status|rev-parse|rev-list|branch\s+-(?:v|vv|a|r|l)?\s*$|describe|fetch|ls-remote|shortlog|blame)\b/.test(part)
      && !/\bgit\s+push\b/.test(part)) {
    continue;
  }
  for (const [re, why] of RULES) {
    if (re.test(part)) {
      console.error(
        `BLOCKED by .claude/hooks/guard-protected-branches.mjs: ${why}.\n` +
        `Command: ${part}\n` +
        `Protected branches: ${PROTECTED.join(", ")}. Ask Logan to run this himself, ` +
        `or work on the 'claude' branch instead.`
      );
      process.exit(2);
    }
  }
}

process.exit(0);
