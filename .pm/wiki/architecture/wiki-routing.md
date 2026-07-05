---
title: Wiki Routing
createdAt: 2026-07-05T07:07:43.7132720Z
modifiedAt: 2026-07-05T07:07:43.7132720Z
---

# Wiki Routing

The local web server maps wiki routes in `WebCommand.MapEndpoints`.

`/wiki` renders the full wiki index. `/wiki/new` renders the create form. `/wiki/edit/{path}` reads an existing page and renders the edit form. `/wiki/{path}` first tries to read a page; if no page exists, it falls back to a folder listing for pages under that path.

Handlers fetch wiki page summaries for shell pages so navigation stays consistent across index, folder, detail, create, and edit views.