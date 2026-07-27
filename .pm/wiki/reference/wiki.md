---
title: Wiki Model and Patching
createdAt: 2026-07-27T06:14:45.2832080Z
modifiedAt: 2026-07-27T06:16:55.9589760Z
---

PM's wiki is a collection of Markdown files under `.pm/wiki/`. The directory structure defines navigation; no separate tree database exists.

## Page format

```markdown
---
title: Development Setup
createdAt: 2026-07-01T10:00:00Z
modifiedAt: 2026-07-01T10:00:00Z
---

Install the required SDKs and restore dependencies.
```

The file `.pm/wiki/guides/setup.md` becomes the path `guides/setup`. Path and title are distinct: moving or renaming a page can change either one.

Frontmatter is PM-owned metadata. The web editor exposes only the Markdown body and provides dedicated metadata controls. CLI full-file editing and the MCP full-document update tool include frontmatter and validate it before saving.

## Search

CLI, MCP, local web, and static web search page title, path, and body case-insensitively. Results include a snippet and deterministic match count/order.

## Targeted MCP patches

For an agent changing one section:

1. Call `outline_wiki_page` with the page path.
2. Select a heading ID from the returned ATX heading outline.
3. Keep the returned body version.
4. Call `patch_wiki_page` with that heading ID, version, Markdown, and operation.

Supported operations append or prepend within a section, replace its body, insert before a heading, or insert after a section. Heading IDs are derived from outline position rather than fragile line numbers. The version guard rejects stale edits.

## Linking conventions

Hosted and static modes currently use different route forms:

```markdown
[CLI Guide](/wiki/guides/cli) <!-- hosted app -->
[CLI Guide](#/wiki/guides/cli) <!-- static export -->
```

The Markdown renderer does not rewrite links between path and hash routing. For content published in both modes, prefer an unlinked page/task name and let readers use the tree or search. A wiki folder remains navigable even when it has no page of its own.

## Editing discipline

- Prefer a narrow patch when only one section changes.
- Use body-only web editing for ordinary prose changes.
- Use rename for path/title changes so created metadata is preserved.
- Run `pm doctor` or MCP `validate_project` after manual file changes.
- Never place secrets in wiki pages; the wiki is intentionally publishable.