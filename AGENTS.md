# Repository Instructions

## Dotnet CLI Usage

In the codex sandbox, run dotnet commands that build in single node mode and without restore.
Use `-m:1 --no-restore` for `dotnet build` and for any command that triggers a build, such as `dotnet test`.
If you do not do this, the builds may sit indefinitely and produce no output.
In Codex/sandboxed sessions, any .NET command that needs NuGet package access or restore, including `dotnet restore`, must run from an elevated shell. Running the built app from Codex may also require an elevated shell.

## Repository Workflow

- Use the PM MCP tools for normal PM project mutations whenever they are available, including creating, editing, moving, reordering, and removing tasks; changing task state or metadata; and updating PM wiki content.
- Do not manually edit `.pm/tasks/`, `.pm/states/`, `.pm/task_order.yaml`, or `.pm/wiki/` during ordinary work. The MCP/application services maintain cross-file invariants such as task ordering. Direct edits are reserved for MCP-unavailable recovery, bootstrapping, or repairing the MCP implementation itself; after any direct edit, run `pm doctor` and resolve all reported inconsistencies before completing the work.
- Commit each completed PM task before beginning the next task. Keep the task implementation, tests, documentation, and associated `.pm` state update in the same commit; do not combine implementations for multiple task IDs in one commit.
- Prefix every task commit subject with its task ID using the format `<TASK-ID>: <imperative summary>`, for example `PM-0048: Add Angular component Storybook`.
- Restore .NET dependencies with elevated shell: `dotnet restore PM.slnx`.
- Build the .NET solution: `dotnet build PM.slnx -m:1 --no-restore`.
- Run .NET tests: `dotnet test PM.slnx -m:1 --no-restore`.
- In Codex/sandboxed sessions, run the CLI locally after build with elevated shell: `dotnet PM/bin/Debug/net10.0/PM.dll <command>`.
- In Codex/sandboxed sessions, start the development API from inside an initialized PM project with elevated shell: `dotnet PM/bin/Debug/net10.0/PM.dll web --api --port 51237`, then run `npm start` in `web/`.
- Run the embedded Angular board from the published release artifact with elevated shell: `dotnet artifacts/release/PM.dll web`.
- Worker dependencies: run `socket npm install` in `next-id-worker/` only when Node tooling needs local install state.
- Worker tests: `npm test` in `next-id-worker/`.
- Worker dev server: `npm run dev` in `next-id-worker/`.
- Worker deploy: `npm run deploy` in `next-id-worker/`.
- Worker D1 migrations: `npm run migrate:local` or `npm run migrate:remote` in `next-id-worker/`.
- Agent-host prerequisites: use Node `26.5.0` from the root `.node-version` and npm 11.
- Agent-host dependencies: run `socket npm install` in `agent-host/`.
- Agent-host formatting: run `npm run format` before completing and committing agent-host changes, then verify with `npm run format:check`.
- Agent-host strict check: run `npm run check` in `agent-host/`.
- Agent-host tests: run `npm test` in `agent-host/`.
- Agent-host production build: run `npm run build` in `agent-host/`.
- Complete agent-host gate: run `npm run validate` in `agent-host/`.
- Agent-host HTTPS tests require OpenSSL and bind a temporary loopback port; run `npm run validate` from an elevated shell in Codex when the sandbox rejects the listener.
- Angular prerequisites: use Node `26.5.0` from the root `.node-version` and npm 11.
- Angular dependencies: run `socket npm install` in `web/`.
- Angular dev server: run `npm start` in `web/`; it proxies `/api` to `http://127.0.0.1:51237`.
- Angular formatting: run `npm run format` in `web/` before completing and committing any task that changes the Angular workspace, then verify it with `npm run format:check`.
- Angular strict build check: run `npm run check` in `web/`.
- Angular tests: run `npm test` in `web/`, or `npm run test:watch` for interactive development.
- Angular production build: run `npm run build` in `web/`.
- Storybook component workshop: run `npm run storybook` in `web/`.
- Storybook browser tests: run `npm run test:storybook` in `web/`; install its Chromium runtime with `socket npx playwright install chromium` when needed.
- Storybook production build: run `npm run build-storybook` in `web/`.
- Angular E2E: run `npm run e2e` in `web/`; it uses isolated temporary projects and a loopback fake next-ID service.
- Embedded Angular smoke: after release publish, run `npm run e2e:embedded` in `web/`.
- Complete frontend gate: run `npm run frontend:validate` in `web/`.
- Complete release gate: run `npm run release` in `web/`; it performs `socket npm ci`, all frontend and .NET validation, embedded publish, and production smoke tests.

Any npm command that installs, updates, removes, or otherwise modifies packages, `package.json`, or lockfiles must use `socket npm ...`. Normal .NET build and test commands do not install or build the Angular workspace. Storybook browser tests use Playwright through `npm run test:storybook`; standalone application E2E uses `npm run e2e`. Use `playwright-cli` for ad hoc UI validation when available.

No dedicated lint command is configured in this repository. Do not invent one. Before work is complete, format Angular or agent-host changes and run the relevant build and tests for the area changed: .NET changes need `dotnet build PM.slnx -m:1 --no-restore` and `dotnet test PM.slnx -m:1 --no-restore`; Angular changes need `npm run format`, `npm run format:check`, and the relevant Angular validation commands; agent-host changes need `npm run format`, `npm run format:check`, `npm run check`, `npm test`, and `npm run build`; worker changes need `npm test` from `next-id-worker/`.

## Architecture

- `PM/` contains the main .NET `net10.0` CLI application. It uses Spectre.Console.Cli for commands and Microsoft DI from `Program.cs`.
- `PM/Project/` owns project discovery, config, task/state file paths, and persistence under a `.pm` project root.
- `PM/Tasks/` contains task model and CLI task commands.
- `PM/Application/` contains service-level behavior such as `TaskService`, `BoardService`, and `AppResult`. Put cross-command workflow logic here rather than in renderers or command handlers.
- `PM/Web/` contains the local web command, JSON API hosting, and embedded Angular asset serving.
- `web/` contains the standalone Angular 22 replacement client. Keep it zoneless, strictly typed, routed, and independent from the normal .NET build until the release integration work explicitly changes that boundary.
- In Angular routed features, keep page components focused on coordinating data, routing, and page state. Extract repeated or independently meaningful regions into focused components with typed signal inputs and outputs; keep trivial one-off markup inline.
- `PM/Mcp/` contains MCP server host, tools, and response shapes.
- `PM/Files/` contains file-system abstractions.
- `PM.Tests/` contains xUnit tests and test helpers. Add tests near the behavior being changed, especially for rendered HTML and file mutation behavior.
- `next-id-worker/` contains the Cloudflare Worker used by the default next-ID service. Its API and trust model are documented in `next-id-worker/README.md`.
- `agent-host/` contains the standalone TypeScript 7 Linux runner foundation. Keep protocol and persistence behavior aligned with `contracts/agent-runs/v1/`, use Node built-ins where sufficient, and keep host configuration and data outside repositories.
- `next_id.cs` is listed in the solution under `/NanoServices/`.

Use existing constructor-injected services and `AppResult`/`AppResult<T>` for application failures. Keep data access through `ProjectRoot`, `TaskService`, `BoardService`, and Worker D1 prepared statements rather than scattering file or database access through UI code. Keep API contracts typed and render user-controlled values through Angular's safe bindings and established Markdown sanitization.

Prefer extending established patterns over introducing new abstractions. Do not add dependencies when the current .NET libraries, Angular primitives, native browser APIs, or Worker runtime APIs are enough. All `npm` and `npx` flows for the Worker should go through the existing package scripts; Wrangler commands are Socket-wrapped there.

## UI Principles

The web UI is the Angular client in `web/`, embedded into published PM artifacts. Keep its styling focused and professional, and do not broadly restyle existing screens unless the task explicitly requires it.

Non-negotiables:

- Keep task content more prominent than navigation, filters, metadata, and controls.
- Preserve dense but readable layouts for real task-board usage.
- Reuse existing Angular components, native browser APIs, and CSS tokens before adding new primitives.
- Keep destructive and advanced actions contextual, confirmable, and keyboard-accessible.
- Do not introduce decorative gradients, broad shadows, arbitrary colors, or excessive card nesting.
- Preserve filters, board context, and immediate feedback after mutations.

1. Content over chrome
   Keep navigation, filters, and metadata visually quieter than task content. Avoid decorative containers when spacing, alignment, typography, and surface contrast are sufficient. Do not wrap every section in a card. Avoid unnecessary shadows, gradients, thick borders, and ornamental effects.
2. Dense but readable
   Optimize task boards and dialogs for efficient scanning. Use compact controls and rows without compromising legibility, visible focus, or practical touch targets. Avoid oversized headings or excessive whitespace that reduces useful task density.
3. Semantic color
   Use the existing CSS token system in `styles.css`. Use accent colors sparingly. Reserve strong color for status, priority, validation, selection, warnings, destructive actions, and meaningful emphasis. Do not introduce arbitrary colors directly in templates or components.
4. Consistent interaction grammar
   The same task should behave consistently in board rows, dialogs, forms, and filtered views. Reuse existing Angular routing, dialog, selection, editing, filtering, and mutation patterns before adding new ones.
5. Progressive disclosure
   Keep common paths visible: filtering, opening a task, creating a task, editing, state changes, and removal flows should remain straightforward. Put advanced or destructive choices in contextual controls, dialogs, confirmations, popovers, drawers, expandable sections, or command interfaces.
6. Contextual controls
   Secondary actions may appear on hover, focus, selection, or overflow menus, but essential actions must remain discoverable. Hover-only actions must also be reachable by keyboard and touch.
7. Keyboard efficiency
   Preserve logical tab order and visible focus. Important repetitive workflows should be keyboard accessible. Add shortcuts only when they do not conflict with text entry, browser behavior, or accessibility. If a command menu is added, expose relevant actions there and show shortcut hints in menus where appropriate.
8. Immediate, trustworthy feedback
   Acknowledge actions immediately. Use localized loading/error states and stable layouts rather than unnecessary full-page loading. Use optimistic updates only when failure can be detected and safely rolled back. Preserve filters and user context after mutations.
9. Motion
   Motion must explain state changes, hierarchy, or spatial relationships. Keep transitions subtle and brief, avoid decorative animation, and respect reduced-motion preferences.
10. Responsive behavior
   Preserve task priority instead of merely shrinking desktop layouts. Collapse or move secondary information before hiding primary actions. Do not depend on hover. Avoid accidental horizontal scrolling except where intentionally required.

## Component Requirements

- Reuse existing Angular components, CSS variables, and controls before creating new primitives.
- Support states that matter for the component's behavior: default, hover, active, selected, focus-visible, disabled, loading, empty, and error where relevant.
- Keep business logic in application services and API endpoints; keep reusable presentation in focused Angular components.
- Prefer composition of focused components over large components with many unrelated boolean options.
- Avoid one-off styling that duplicates an existing selector or token.
- Keep variants intentional and limited.
- Ensure icons, if introduced, have consistent sizing, alignment, stroke treatment, accessible labels, and tooltips for icon-only buttons.
- Keep user-controlled content safely rendered and URL-encoded through established Angular helpers.

## Accessibility

Accessibility is part of completion, not a later enhancement. Use semantic HTML or framework equivalents, keyboard-operable interactions, visible focus states, correct labels and accessible names, and ARIA only where native semantics are insufficient. Maintain sufficient contrast, support reduced motion, announce meaningful asynchronous changes where necessary, and never communicate critical information by color alone.

## Performance

Avoid unnecessary re-renders, repeated expensive computation, avoidable layout shift, and full-page reloads for local fragment updates. Lazy-load only where it meaningfully improves startup cost. Use virtualization for genuinely large collections when appropriate. Debounce or cancel rapid query-driven interactions when needed. Measure before introducing complex performance optimizations.

## Implementation Discipline

- Inspect nearby code before modifying a feature.
- Follow existing naming, file organization, dependency injection, result handling, and testing conventions.
- Make the smallest coherent change that solves the problem.
- Avoid unrelated refactoring and broad visual restyling.
- Explain significant architectural deviations.
- Remove obsolete code introduced by the change.
- Update documentation and tests when behavior changes.
- Preserve the Worker trust model: `.pm/project_id.txt` is a public project identifier, local PM identity private keys stay in OS user config outside `.pm/`, and Worker errors must not log credentials, signatures, recovery keys, or request paths.
- Do not commit generated artifacts such as `bin/`, `obj/`, `node_modules/`, `.wrangler/`, or local D1 database files.

## Validation Checklist

Before declaring UI work complete, verify:

- The primary workflow is visually obvious.
- Secondary chrome does not compete with task content.
- The screen remains useful at realistic data density.
- Keyboard navigation and focus behavior work.
- Loading, empty, error, disabled, and permission-restricted states are handled.
- Responsive layouts preserve primary actions.
- Interaction feedback is immediate and understandable.
- Existing components, helpers, and tokens were reused where possible.
- Relevant .NET and/or Worker tests pass, along with the .NET build when application code changes.
- No unrelated behavior or visual regressions were introduced.

## Review Guidance

When reviewing changes, prioritize:

1. correctness and data safety
2. accessibility
3. workflow efficiency
4. consistency with existing interaction patterns
5. information hierarchy
6. performance
7. visual polish

Flag unnecessary visual ornamentation, excessive card usage, inconsistent spacing, hard-coded styling values, hidden essential actions, inaccessible custom controls, unsafe HTML rendering, secret leakage, and new patterns that duplicate established ones.
