# Quarry v0.3.2

_Released 2026-04-24_

**Fixes a QRY044 false-positive that affected every realistic `<InterceptorsNamespaces>` setup.** Because `build/Quarry.targets` auto-appends `Quarry.Generated`, every consumer's evaluated list has at least two entries — and QRY044 was incorrectly firing on every one of them. 1 PR merged since v0.3.1.

---

## Highlights

- **QRY044 false-positive fixed** — the analyzer now correctly recognizes multi-entry `<InterceptorsNamespaces>` lists. Previously, any `.csproj` with more than one entry (essentially every real project) got a spurious warning even with the correct namespace opted in.

---

## Bug Fixes

### Analyzer False Positives

- **QRY044 fires on every multi-entry `<InterceptorsNamespaces>` list** (#265, closes #264). Root cause: Roslyn's editorconfig key-value parser treats `;` and `#` inside a value as inline-comment markers, so `build_property.InterceptorsNamespaces` read only the text up to the first `;` (or an empty string if the list began with `;`). The analyzer now prefers a new pipe-delimited `build_property.QuarryInterceptorsNamespaces`, computed in a target that runs before `GenerateMSBuildEditorConfigFileCore` and declared in both `Quarry.Generator.props` and `Quarry.targets` so the fix applies whether or not `Quarry.Generator` is in the package graph. Legacy semicolon-delimited property is still read as a fallback so older Quarry packages keep working on the single-entry happy path. No csproj changes required; any `<NoWarn>QRY044</NoWarn>` suppression added as a workaround becomes dead but harmless.

---

## Stats

- 1 PR merged since v0.3.1
- 0 new diagnostics
- 0 breaking changes

---

## Full Changelog

### Bug Fixes

- Fix QRY044 false-positive when InterceptorsNamespaces has multiple entries (#265)
