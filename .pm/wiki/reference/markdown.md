---
title: Markdown Showcase
createdAt: 2026-07-27T06:14:45.2879570Z
modifiedAt: 2026-07-27T06:16:55.9442380Z
---

This page exercises the Markdown features used by task descriptions and wiki pages. It is both a writing reference and a visual regression fixture for local and static rendering.

# Heading level 1

Heading level 1 is available for imported documents, though normal wiki pages already display their title above the body.

## Heading level 2

### Heading level 3

#### Heading level 4

##### Heading level 5

###### Heading level 6

## Inline text

Plain text can contain **bold emphasis**, *italic emphasis*, ***combined emphasis***, ~~strikethrough~~, and `inline code`.

Characters can be escaped when they should remain literal: \*not italic\*, \# not a heading, and \[not a link\].

A normal external link: [GitHub](https://github.com/chronium/pm).

Internal route syntax depends on deployment mode. Hosted mode uses `/tasks/PM-0008`; static mode uses `#/tasks/PM-0008`. See **Wiki Model and Patching** for the current linking convention.

## Paragraphs and line breaks

This is one paragraph with enough text to demonstrate wrapping at typical reading widths. Markdown keeps prose fluid while the application controls readable measure, spacing, contrast, and typography.

This is a second paragraph.\
The previous line ends with two spaces, so this sentence begins after a deliberate line break.

## Blockquote

> Project state should be understandable from the repository.
>
> A quote can contain multiple paragraphs and **formatted text**.

## Lists

Unordered:

- First item
- Second item
  - Nested detail
  - Another nested detail
- Third item

Ordered:

1. Inspect the task.
2. Implement the change.
3. Validate the result.
4. Update project state.

Task list:

- [x] Store project state as files
- [x] Expose CLI, MCP, web, and static modes
- [ ] Continue refining the product

## Code

```sh
pm task search "markdown state:todo in:all" --limit 20
pm wiki show reference/markdown
```

```csharp
public AppResult<WikiPage> GetPage(string path)
{
    return wikiService.GetPage(path);
}
```

```typescript
const selected = computed(() => tasks().filter((task) => task.state !== 'done'));
```

```json
{
  "tool": "get_next_task",
  "arguments": { "readyOnly": true }
}
```

## Table

| Surface | Read | Write | Typical use |
| :--- | :---: | :---: | ---: |
| CLI | Yes | Yes | Terminal |
| MCP | Yes | Yes | Agents |
| Web | Yes | Yes | Interactive work |
| Static site | Yes | No | Publishing |

## Separator

Content above the separator belongs to the main demonstration.

---

Content below it verifies horizontal-rule spacing.

## Long content

A long identifier should remain readable without breaking its container: `PM-THIS-IS-A-LONG-DEMONSTRATION-IDENTIFIER-0001`.

A long URL should wrap safely: https://example.com/a/very/long/path/that/exercises/wrapping/without/requiring/horizontal/page/scrolling.

## Safety

Markdown output is rendered with `marked` and sanitized with DOMPurify in the Angular client. User-authored HTML must never be trusted as an application control or script source.
