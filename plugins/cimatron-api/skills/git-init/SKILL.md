---
name: git-init
description: Offer to initialize a git repository in a freshly-scaffolded Cimatron API plugin folder. Prompts the user once (default Yes); on accept, runs `git init`, writes a sensible `.gitignore` for the .NET/Cimatron template, stages everything, and makes an initial commit. Skips silently with a one-line note if the folder is already inside a git repo. Invoked automatically by `/new-cimatron-api` after the icon step and runnable standalone via `/cimatron-api:git-init`.
argument-hint: [<path>] [--yes] [--no]
---

Offer to put a freshly-scaffolded Cimatron API plugin under git, with one initial commit so the user has a clean rollback point before they start changing things.

Arguments: $ARGUMENTS

## Inputs

- `<path>` — optional. Folder to operate in. If omitted, use the current working directory. Callers like `/new-cimatron-api` pass the scaffolded project folder explicitly so the skill doesn't depend on `cd` state.
- `--yes` — skip the prompt and initialize. Used by callers that have already collected consent.
- `--no` — skip the prompt and do nothing. Used by callers that want to suppress the step entirely (e.g. CI).

If both `--yes` and `--no` are present, error out — the caller has a bug. Don't silently pick one.

## Workflow

### 1. Resolve the target folder.

If `<path>` was passed, use it verbatim. Otherwise use the current working directory.

Validate it exists and is a directory. If not, stop with a one-line error — don't try to create it. The caller is supposed to have scaffolded into it already.

### 2. Detect existing git state.

Run, from inside the target folder:

```powershell
git rev-parse --is-inside-work-tree 2>$null
```

- **Exit 0, stdout `true`:** the folder is already inside a git repository (either it's a repo itself, or it lives inside one — e.g. a monorepo). **Stop with a one-line note** like `git-init: already inside a git repo at <toplevel> — skipping.` (Use `git rev-parse --show-toplevel` to get `<toplevel>`.) Don't prompt, don't double-init.
- **Non-zero exit:** the folder isn't tracked. Proceed.

If `git` itself isn't on PATH, stop with `git-init: git not found on PATH — skipping. Run /setup-env to install git, then re-run this skill.` Don't try to install git inline.

### 3. Prompt the user (unless `--yes` / `--no` was passed).

Ask **once**, defaulting to **yes**:

```
Initialize a git repository in <folder>? [Y/n]
```

- Empty input, `Y`, `y`, `yes` → proceed to init.
- `N`, `n`, `no` → stop with `git-init: skipped by user.` Don't loop, don't re-ask.
- Anything else → treat as "no" and stop with the same message. Don't try to be clever.

If `--yes` was passed, skip the prompt and proceed. If `--no` was passed, skip the prompt and stop with `git-init: skipped (--no).`.

### 4. Run init + initial commit.

Issue these in sequence (each depends on the previous one's success). All commands run with the target folder as the working directory.

```powershell
git init
```

```powershell
# Write .gitignore only if one doesn't already exist — the template might ship one in the future.
$gi = Join-Path $target '.gitignore'
if (-not (Test-Path $gi)) {
    @'
# Build output
bin/
obj/

# VSCode / VS user state
.vs/
*.user
*.suo

# Rider / JetBrains
.idea/
*.DotSettings.user

# NuGet
*.nupkg
packages/

# Misc OS noise
Thumbs.db
.DS_Store
'@ | Set-Content -Path $gi -Encoding utf8
}
```

```powershell
git add -A
```

```powershell
git -c commit.gpgsign=false commit -m "Initial scaffold via /cimatron-api:new-cimatron-api"
```

The `-c commit.gpgsign=false` is **only** for this initial commit. Some users have global gpg signing on and an unconfigured key here would block the commit with a non-obvious error. Disabling signing for this one commit is the friendlier default. If the user wants signed history, every subsequent commit they make themselves uses their own config — we're not changing anything globally.

If `git commit` fails because `user.name` / `user.email` aren't set, fall back to a one-time local identity using the Claude Code user context if available, otherwise print the exact commands the user needs to run (`git config user.name "..."` and `git config user.email "..."`) and stop. Don't invent values.

### 5. Report what happened.

Print a one-line summary the caller can include in its own report:

- On init: `git-init: initialized repo at <folder> with initial commit <shortsha>.`
- On skip-already-repo: `git-init: already inside <toplevel> — skipped.`
- On skip-by-user: `git-init: skipped by user.`
- On skip-by-flag: `git-init: skipped (--no).`

Don't print the full `git status` or commit body — the caller will surface what it needs to.

## When to invoke this skill

- **As a standalone command** (`/cimatron-api:git-init`), when the user wants to git-ize an existing scaffolded folder they didn't init at scaffold time.
- **As a sub-step of `/new-cimatron-api`**, after the icon step and before the final summary. Pass the just-scaffolded project folder as `<path>` so the skill is unambiguous about where to act.

## Things to avoid

- **Don't `git add -A` blindly without writing a `.gitignore` first.** Otherwise the `bin/` and `obj/` from any incidental build slip into the initial commit and pollute history immediately.
- **Don't init in a folder that's already inside a git repo.** Nested repos confuse every tool downstream (VSCode, the user's CI, `git submodule status`). Detect, report, stop.
- **Don't change the user's global git config.** Sign-off, gpg signing, default-branch name, commit template — all of those are user-scope decisions. The only `-c` we set is `commit.gpgsign=false`, and it's per-command, not stored.
- **Don't push to a remote.** This skill doesn't know about remotes. The user adds one themselves if they want.
- **Don't loop the prompt.** One ask, one outcome. If the user types something unparseable, treat it as a "no" and move on — they can always re-run `/cimatron-api:git-init` later.
- **Don't try to recover from `git init` mid-flight failures by rolling back.** If `git init` succeeded but `git commit` failed, leave the half-initialized repo in place and surface the exact failure. The user can fix `user.email` and re-run `git commit -m ...` themselves; deleting the `.git/` they just got is more destructive than the half-state.
