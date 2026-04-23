# Prepare Tagged Release — LLM Skill

Prepare a new tagged Quarry release: validate documentation against code changes since the previous tag, write release notes, bump the version, commit, tag, push, wait for CI, then publish the release body on GitHub.

Repository: `Dtronix/Quarry`. All `gh` commands MUST include `--repo Dtronix/Quarry`.

Prerequisites:
- Read `llm.md` for project surface and package layout.
- `gh auth status` succeeds.
- On `master`, clean working tree, not behind `origin/master`.

This skill is **re-entrant**. If a prior run committed/tagged/pushed but did not finish, it resumes from the correct phase (see Phase 0).

Phase map:

- **0** Preflight + resume detection
- **1** Collect commits + PRs since previous tag
- **2** Classify, map to docs, propose (rich approval, in place)
- **3** Apply doc edits → version bump → commit (`Release vX.Y.Z`)
- **4** Annotated tag → **HARD GATE** → push commit + tag
- **5** Wait for `ci.yml` (ignore `benchmark.yml`)
- **6** **HARD GATE** → replace GitHub release body with local notes

Hard-gate rule: never proceed past a **HARD GATE** on implicit signal. Require the user to explicitly answer "yes" / "push" / "publish". Abort on anything else.

---

## Phase 0: Preflight + Resume Detection

Run every check. Abort on any failure with the exact diagnostic.

```bash
git rev-parse --abbrev-ref HEAD                # must be: master
git status --porcelain                         # must be empty
git fetch origin master --tags
git rev-list --left-right --count origin/master...HEAD   # must be: 0 0
gh auth status
```

Resolve baseline:

```bash
PREV_TAG=$(git tag --list 'v*' --sort=-v:refname | head -1)
PREV_VERSION=${PREV_TAG#v}
PROPS_VERSION=$(grep -oP '(?<=<Version>)[^<]+' Directory.Build.props)
```

**Drift check.** If `PROPS_VERSION` != `PREV_VERSION`, abort:

```
ABORT: version drift
  Directory.Build.props <Version>: <PROPS_VERSION>
  Last git tag:                    <PREV_TAG>
Likely causes:
  - a previous release bumped props but never tagged
  - props was edited manually
  - tag was placed on the wrong commit
Resolve manually before re-running.
```

If no `v*` tags exist at all, this is the first release — use the root commit as the baseline and apply the initial-release variant of the notes template (see Appendix). Do not abort on drift in that case.

**Resume detection.** Probe world-state in this order and jump to the matching phase:

| Check | Resume at |
|---|---|
| Local `HEAD` matches a `vX.Y.Z` tag AND `gh release view vX.Y.Z` body already matches `docs/articles/releases/release-notes-vX.Y.Z.md` (stripped per Phase 6) | Done — report and exit |
| Tag exists on remote, release exists, body does NOT match local notes | Phase 6 |
| Tag exists on remote, no release yet (CI still running or failed) | Phase 5 |
| Tag exists locally, not on remote | Phase 4 push |
| `HEAD` commit subject is `Release vX.Y.Z` AND no tag | Phase 4 tag |
| None of the above | Phase 1 |

Commands:

```bash
LOCAL_TAG=$(git tag --points-at HEAD | grep '^v' | head -1)
REMOTE_TAG=$(git ls-remote --tags origin "refs/tags/$LOCAL_TAG" 2>/dev/null)
HEAD_SUBJECT=$(git log -1 --pretty=%s)
gh release view "$LOCAL_TAG" --repo Dtronix/Quarry --json body 2>/dev/null
```

Report which phase you're resuming at and why before proceeding.

---

## Phase 1: Collect

Gather every change since `PREV_TAG`. Two passes: commits and PRs. Map PRs back to commits so each commit is accounted for exactly once.

**Commits:**

```bash
git log "$PREV_TAG..HEAD" --no-merges --pretty=format:'%H%x09%s%x09%an'
git log "$PREV_TAG..HEAD" --merges --pretty=format:'%H%x09%s'
```

**PRs:**

```bash
PREV_DATE=$(git log -1 --format=%cI "$PREV_TAG")
gh pr list --repo Dtronix/Quarry --state merged \
  --search "merged:>=$PREV_DATE" --limit 200 \
  --json number,title,body,labels,mergeCommit,files,author,mergedAt
```

Map each PR to its merge commit via `mergeCommit.oid`. For squash merges without a merge commit, fall back to matching `(#\d+)$` on the commit subject. Every PR must resolve to exactly one commit in the `$PREV_TAG..HEAD` range.

**PR-less commits**: any commit in the range not associated with a PR. Record separately; Phase 2 decides whether to inline them thematically or list under "Direct commits" at the bottom of the release notes.

For each PR, also fetch changed files (already in the JSON via `files`). For PR-less commits:

```bash
git show --stat --name-only <sha>
```

Do not analyze diffs in this phase beyond file lists — Phase 2 classification uses file paths + PR body + commit message, not full diff contents, unless a specific change is ambiguous.

---

## Phase 2: Propose (rich approval, in place)

Internal classification and doc-impact mapping happen here, then surface to the user as a single proposal. If the user asks for a rewrite, edit in place — do not restart Phase 1 or re-run classification.

### 2.1 Classify each change

Labels (one per change):

- `breaking` — removed/renamed public API, changed public signature, touched `llm-migrate.md`, `BREAKING` in PR title/body/label.
- `added` — new public API, new package, new CLI command, new sample.
- `changed` — behavior change to existing public API that is not breaking.
- `fixed` — bug fix.
- `performance` — perf improvement, no behavior change.
- `architecture` — internal refactor of the compiler pipeline or runtime shape that affects how things work but not the surface.
- `docs` — docs-only change.
- `internal` — test, tooling, build, CI, code movement.

### 2.2 Map file paths → affected docs

| Changed paths | Docs to review |
|---|---|
| `src/Quarry/**` (runtime, schema DSL, executor, context) | `llm.md`, `README.md`, relevant `docs/articles/*.md` (context-definition, querying, modifications, prepared-queries, logging, switching-dialects, sql-manifest) |
| `src/Quarry.Generator/**` | `src/Quarry.Generator/llm.md`, `src/Quarry.Generator/README.md`, `docs/articles/scaffolding.md`, `docs/articles/schema-definition.md`, `docs/articles/getting-started.md` |
| `src/Quarry.Analyzers/**` (non-CodeFixes) | `src/Quarry.Analyzers/README.md`, `docs/articles/analyzer-rules.md`, `docs/articles/diagnostics.md` |
| `src/Quarry.Analyzers.CodeFixes/**` | `src/Quarry.Analyzers.CodeFixes/README.md` |
| `src/Quarry.Migration/**` | `src/Quarry.Migration/README.md`, `llm-migrate.md`, `docs/articles/migrating-to-quarry.md`, `docs/articles/migrations.md` |
| `src/Quarry.Tool/**` | `src/Quarry.Tool/README.md`, `docs/articles/migrations.md`, `docs/articles/scaffolding.md` |
| `src/Quarry.Benchmarks/**` | `docs/articles/benchmarks.md` |
| `docs/**` | (docs-only; no cross-update needed) |
| `.github/**`, `*.csproj`, `Directory.Build.*` | no docs (unless user-facing behavior changes) |

Any `breaking` change must touch `llm-migrate.md` and produce a "Migration Guide from vPREV" entry in the release notes.

Classify each change as `needs-doc-update` (list target docs + one-line proposed edit) or `no-doc-impact` (one-line reason).

### 2.3 Assemble draft release notes

Build the release notes skeleton using the Appendix template. Keep the Full Changelog exhaustive (every PR + every meaningful PR-less commit). PR-less commits that fit a thematic section go inline with their short SHA in place of `(#N)`; the rest collect under a `### Direct commits` subsection at the end of Full Changelog.

### 2.4 Present the proposal

Format:

```
## Proposal — Release v<suggested>

### Docs to update
<target-doc-path>
  - [PR#/sha] <one-line change> → <proposed edit>
  - …

### Docs with no impact
<target-doc-path>
  - [PR#/sha] <reason no doc change needed>

### Release notes skeleton
<the full skeleton, section by section>

### PR-less commits
- inlined: [sha] <title> → <section>
- direct commits: [sha] <title>
```

### 2.5 Approval loop

Accept rich edits: "skip item 3", "rewrite the highlight on CTEs to X", "move PR #234 from architecture to performance", "drop the 'Direct commits' section", etc. Apply edits in place and re-display only what changed. Loop until the user says "approved" / "looks good" / equivalent explicit affirmation. Do not advance on ambiguous replies.

---

## Phase 3: Finalize (edits + version + commit)

### 3.1 Apply doc edits

Apply every approved edit to the target docs.

Also create/update:

- `docs/articles/releases/release-notes-vX.Y.Z.md` — full release notes using the Appendix template. Title and `_Released YYYY-MM-DD_` line use a placeholder until 3.2 resolves the version; date is `$(date +%Y-%m-%d)` evaluated at commit time (step 3.4).
- `docs/articles/releases/index.md` — prepend a new row to the table under the existing header.
- `docs/articles/releases/toc.yml` — prepend a new entry with the new version.

Stage everything so far:

```bash
git add -- llm.md llm-migrate.md src/**/llm.md README.md \
          src/Quarry*/README.md docs/ \
          # and any specific files touched
```

Do NOT commit yet.

### 3.2 Version bump prompt

Show the current version and offer:

```
Current version: X.Y.Z

Pick a bump:
  1. major → (X+1).0.0
  2. minor → X.(Y+1).0
  3. patch → X.Y.(Z+1)
  4. custom: ____
```

If any `breaking` changes were classified in Phase 2.1, prepend a warning block listing them:

```
⚠ Breaking changes detected (semver suggests major bump):
  - [PR#] <summary>
  - …
```

Warn only — accept whatever the user picks.

### 3.3 Apply version + resolve release-notes placeholders

- Update `Directory.Build.props` `<Version>` to the new version.
- In `release-notes-vX.Y.Z.md`: replace the title placeholder with `vX.Y.Z` and set `_Released <today>_` using `date +%Y-%m-%d`.
- In `index.md` / `toc.yml`: replace the placeholder version with the concrete version.
- Rename `release-notes-vX.Y.Z.md` if it was created with a placeholder filename.

Stage these changes:

```bash
git add Directory.Build.props docs/articles/releases/
```

### 3.4 Combined diff review

Show the full staged diff (or a summary with `git diff --staged --stat` if very large, plus per-file diffs on request). This is the single consolidated review gate for both documentation edits and the version bump.

Ask explicit confirmation. On "make changes" requests, edit in place, re-stage, re-show. On explicit approval:

### 3.5 Commit

```bash
git commit -m "Release vX.Y.Z"
```

One-liner subject. No body. Do NOT use `--amend` under any circumstance.

---

## Phase 4: Tag + Push (HARD GATE)

### 4.1 Annotated tag

```bash
git tag -a "vX.Y.Z" -m "Release vX.Y.Z"
```

Annotated (not lightweight) — carries author, date, and message.

### 4.2 HARD GATE

Display:

```
About to push to origin:
  - commit: <short-sha> "Release vX.Y.Z"
  - tag:    vX.Y.Z

This will:
  - make the commit public on master
  - trigger ci.yml (build + auto-create GitHub release)
  - trigger benchmark.yml (ignored by this skill; completes separately)

Proceed? (requires explicit "push" to continue)
```

Wait for explicit affirmation. Abort otherwise.

### 4.3 Push

Push commit and tag as separate commands so failure modes are distinguishable:

```bash
git push origin master
git push origin "vX.Y.Z"
```

If the commit push fails, stop — do not push the tag. If the tag push fails but the commit succeeded, report and stop; the user can re-run the skill (Phase 0 resume detection will pick up at Phase 4).

---

## Phase 5: Wait for CI

Find the CI run triggered by the tag push, then block until it finishes. Filter to `ci.yml`; ignore `benchmark.yml` regardless of its state.

```bash
TAG_SHA=$(git rev-parse "vX.Y.Z^{commit}")
RUN_ID=$(gh run list --repo Dtronix/Quarry --workflow=ci.yml \
         --commit "$TAG_SHA" --limit 1 --json databaseId \
         --jq '.[0].databaseId')
```

If `RUN_ID` is empty, wait 10s and retry up to 6 times (GitHub can lag creating the run after push). If still empty, abort with:

```
No ci.yml run found for commit <TAG_SHA>. Check https://github.com/Dtronix/Quarry/actions manually.
```

Block on the run:

```bash
gh run watch "$RUN_ID" --repo Dtronix/Quarry --exit-status
```

If `gh run watch` fails due to network (non-zero exit but run still running), fall back to a poll loop:

```bash
while :; do
  STATUS=$(gh run view "$RUN_ID" --repo Dtronix/Quarry --json status,conclusion --jq '.status + ":" + (.conclusion // "")')
  case "$STATUS" in
    completed:success) break ;;
    completed:*)       echo "CI failed: $STATUS"; exit 1 ;;
    *)                 sleep 20 ;;
  esac
done
```

On CI failure: do NOT edit the release. Report the failing job URL (`gh run view "$RUN_ID" --repo Dtronix/Quarry --log-failed | head -50`) and stop. The user fixes forward, pushes again, and re-runs the skill (Phase 0 resumes at Phase 5).

---

## Phase 6: Publish Release Body (HARD GATE)

`ci.yml` auto-creates the GitHub release on `v*` tag push (see `.github/workflows/ci.yml`). This phase replaces that auto-created body with our curated notes.

### 6.1 Verify release exists

```bash
gh release view "vX.Y.Z" --repo Dtronix/Quarry --json body --jq '.body' > /tmp/current-body.md
```

If it doesn't exist, abort — something about CI is different from expectations.

### 6.2 Build GitHub body

The release body is the docs file with the leading title lines stripped (GitHub renders its own title):

```bash
# Strip "# Quarry vX.Y.Z" and the following "_Released YYYY-MM-DD_" line + blank
sed '1,3d' docs/articles/releases/release-notes-vX.Y.Z.md > /tmp/release-body.md
```

Verify the first 3 stripped lines look correct; if not, adjust. The committed docs file is the canonical source — never edit the docs file to match.

### 6.3 HARD GATE

Show the diff between current release body and the new body:

```bash
diff -u /tmp/current-body.md /tmp/release-body.md | head -200
```

Display and ask explicit confirmation:

```
Replace the v<X.Y.Z> release body on Dtronix/Quarry?
  - Current body: <N> lines, auto-generated by CI
  - New body:     <N> lines, from docs/articles/releases/release-notes-vX.Y.Z.md

Proceed? (requires explicit "publish" to continue)
```

### 6.4 Publish

```bash
gh release edit "vX.Y.Z" --repo Dtronix/Quarry --notes-file /tmp/release-body.md
```

Verify:

```bash
gh release view "vX.Y.Z" --repo Dtronix/Quarry --json body --jq '.body' | diff -u - /tmp/release-body.md
```

Report the release URL and the local docs path. Done.

---

## Appendix: Release Notes Template

Matches the structure established by `release-notes-v0.2.0.md` and `release-notes-v0.3.0.md`. Every section is optional except Highlights, Stats, and Full Changelog — omit empty sections rather than leaving placeholders.

```markdown
# Quarry vX.Y.Z

_Released YYYY-MM-DD_

**<one-sentence hook, key themes in bold>** <expansion paragraph, closes with "N commits merged since vPREV.">

---

## Highlights

- **<theme>** — <one-line what-and-why>
- … (8–10 bullets; bold the feature name)

---

## Breaking Changes

### API Changes

- **`<thing>`** (#PR). <what changed, with before/after code snippet if non-trivial>
- …

### Diagnostic / Analyzer Changes

- …

### Opt-In Upgrades

- …

---

## New Features

### Query Engine

#### <Feature> (#PR[, #PR])

```csharp
// concise example
```

- bullet details
- …

### Migration Framework / Analyzers / Tooling
(sections as needed)

---

## Performance

### <Named change> (#PR)

Prose with before/after numbers when available.

---

## Architecture

### <Named change> (#PR)

---

## Bug Fixes

### SQL Correctness
- **<bug>** (#PR). <description>

### Code Generation
- …

### Runtime / Analyzer False Positives / Security
- …

---

## Documentation & Tooling

- bullets

---

## Migration Guide from vPREV

### Required Changes

1. **<thing>** — <before/after snippet>
2. …

### Optional Improvements

- …

---

## Stats

- N commits / PRs merged since vPREV
- Diagnostics added / retired
- New packages / samples / tests

---

## Full Changelog

### <Group — e.g., Chain API Additions>
- <PR title> (#N)
- …

### <Group — e.g., Bug Fixes>
- …

### Direct commits
(only if present — PR-less commits that did not fit a thematic group)
- <commit subject> (<short-sha>)
```

### Initial-release variant

If no prior `v*` tag exists, use the `v0.1.0` shape instead:

- Product overview + "why" paragraph
- Optimization tiers table (if applicable)
- Packages section (per-package: install snippet + "install if")
- Quick look with code samples
- Comparison table
- Supported databases table
- Getting started install commands

Omit Breaking Changes, Migration Guide, and Stats sections — everything is new.
