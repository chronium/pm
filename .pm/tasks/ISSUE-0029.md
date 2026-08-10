---
id: ISSUE-0029
title: Prevent mobile project title collisions in the top bar
track: ISSUE
milestone: angular-web
createdAt: 2026-08-10T06:09:24.3867150Z
modifiedAt: 2026-08-10T06:09:24.3867150Z
---

On narrow mobile screens, the project title can overlap the Overview, Tasks, and Wiki navigation. It also remains visible beneath the expanded mobile search surface.

Goal:

Keep the top bar readable and operable at compact widths without unconditionally removing project identity from every mobile layout.

Acceptance criteria:

- The project title remains in the top bar while the available width can accommodate it without overlap.
- Below the compact-width threshold, project identity and switching move into the hamburger navigation instead of colliding with workspace navigation or actions.
- Opening task or wiki search presents an unobstructed search surface; the project title cannot show through or overlap it.
- Overview, Tasks, Wiki, search, read-only context, and theme controls remain visible or intentionally accessible through the compact navigation.
- The top bar has no horizontal overflow at 320px, 375px, 390px, and 430px with representative short and long project names.
- Live and static modes retain keyboard, touch, focus, and accessible-name behavior.
- This task fixes the concrete top-bar collision only; a broader mobile UI audit remains separate.