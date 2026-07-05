---
title: Renderer Flow
createdAt: 2026-07-05T07:07:43.6635660Z
modifiedAt: 2026-07-05T07:07:43.6635660Z
---

# Renderer Flow

The web board renders wiki pages through `BoardHtmlRenderer`, using embedded templates from `PM/Web/Templates` and shared CSS from `PM/Web/Templates/Assets/styles.css`.

Wiki shell pages share the same top bar and wiki sidebar. The page body stays focused on the current view: index, folder listing, detail page, create form, or edit form.

The wiki sidebar tree is built from `WikiPageSummary.Path` values. Intermediate path segments become folder disclosure rows, while leaf pages render as navigation links using their page titles.