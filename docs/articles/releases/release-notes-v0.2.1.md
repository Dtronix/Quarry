# Quarry v0.2.1

_Released 2026-03-29_

A patch release adding `ownsConnection` support to `QuarryContext` and documentation improvements.

---

## New Features

### `ownsConnection` Support for QuarryContext (#128)

Enable the context to own and dispose its underlying `DbConnection`, enabling clean DI registration where consumers don't need to manage connection lifetime:

```csharp
services.AddScoped<AppDb>(_ =>
    new AppDb(new SqliteConnection(connectionString), ownsConnection: true));
```

- When `ownsConnection: true`, context disposes the connection on `Dispose`/`DisposeAsync`.
- When `ownsConnection: false` (default), existing behavior preserved — only closes if context opened it.
- Generator emits new constructor overloads on generated context classes.
- Zero overhead — single `bool` field read in dispose path.

---

## Documentation & Tooling

- Redesign landing page: compact hero, documentation links first.
- Show article sidebar on documentation landing page.
- Add LLM migration skill and migration documentation article.
- Update README: Quarry is now fully reflection-free.
- Fix LLM docs: update context examples from `IQueryBuilder<T>` properties to `IEntityAccessor<T>` methods.

---

## Full Changelog

- Add ownsConnection support to QuarryContext (#128)
- Redesign landing page: compact hero, documentation links first
- Show article sidebar on documentation landing page
- Add LLM migration skill and migration documentation article
- Update README: Quarry is now fully reflection-free
