import { mkdir, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';

export const e2eRoot = process.env.PM_E2E_ROOT;
if (!e2eRoot) throw new Error('PM_E2E_ROOT must be set by the Playwright configuration.');

export const projectRoot = join(e2eRoot, 'project');
export const configRoot = join(e2eRoot, 'config');
export const linkedProjectRoot = join(projectRoot, 'linked');
export const siblingProjectRoot = join(projectRoot, 'sibling');
const idPort = process.env.PM_E2E_ID_PORT;
if (!idPort) throw new Error('PM_E2E_ID_PORT must be set by the E2E runner.');

const timestamp = '2026-01-01T00:00:00.0000000Z';

export async function resetFixture(size = 'small') {
  const pm = join(projectRoot, '.pm');
  await rm(pm, { recursive: true, force: true });
  await rm(linkedProjectRoot, { recursive: true, force: true });
  await rm(siblingProjectRoot, { recursive: true, force: true });
  await mkdir(projectRoot, { recursive: true });
  await Promise.all([
    mkdir(join(pm, 'tasks'), { recursive: true }),
    mkdir(join(pm, 'states', 'todo'), { recursive: true }),
    mkdir(join(pm, 'states', 'in-progress'), { recursive: true }),
    mkdir(join(pm, 'states', 'done'), { recursive: true }),
    mkdir(join(pm, 'wiki'), { recursive: true }),
    mkdir(configRoot, { recursive: true }),
  ]);

  await writeFile(
    join(pm, 'pm_config.yaml'),
    `name: Playwright Project
idWidth: 4
idPrefix: E2E
nextIdServiceUrl: http://127.0.0.1:${idPort}
taskStates:
  todo: To Do
  in-progress: In Progress
  done: Done
tracks:
  E2E: Product
  OPS: Operations
milestones:
  current:
    title: Current Release
    description: ''
    priority: high
    requiredActivationTriggers: []
    delivery: null
  later:
    title: Later
    description: ''
    priority: none
    requiredActivationTriggers: []
    delivery: null
`,
  );
  await writeFile(join(pm, 'project_id.txt'), 'playwright-project\n');
  if (process.env.PM_E2E_MODE === 'dev') await createLinkedFixtures(pm);
  if (process.env.PM_E2E_MODE === 'static') {
    await writeFile(
      join(pm, 'linked_projects.yaml'),
      `version: 1
children:
  - projectId: published-project
    alias: published
    repositoryUrl: https://example.test/published.git
    pathHint: missing-published
    publicSiteUrl: http://127.0.0.1:${process.env.PM_E2E_UI_PORT}/published/?source=fixture#old
  - projectId: unavailable-project
    alias: unavailable
    repositoryUrl: https://example.test/unavailable.git
    pathHint: missing-unavailable
`,
    );
  }

  const count = size === 'large' ? 180 : 6;
  for (let number = 1; number <= count; number += 1) {
    const id = `E2E-${String(number).padStart(4, '0')}`;
    const state =
      size === 'large' && number <= 120 ? 'done' : number % 5 === 0 ? 'in-progress' : 'todo';
    const track = number % 3 === 0 ? 'OPS' : 'E2E';
    const milestone = number % 4 === 0 ? 'later' : 'current';
    const dependency =
      number > 1 && number % 6 === 0
        ? `dependsOn:\n- ${
            process.env.PM_E2E_MODE === 'static'
              ? `pm://project/playwright-project/task/E2E-${String(number - 1).padStart(4, '0')}`
              : `E2E-${String(number - 1).padStart(4, '0')}`
          }\n`
        : '';
    const longDescription = [1, 3].includes(number)
      ? `\n\n${Array.from(
          { length: 12 },
          (_, index) =>
            `## Section ${index + 1}\n\nLong fixture content verifies that task metadata follows the complete description.\n\n- Preserve headings\n- Preserve lists\n- Preserve document flow`,
        ).join('\n\n')}`
      : '';
    await writeFile(
      join(pm, 'tasks', `${id}.md`),
      `---
id: ${id}
title: ${size === 'large' ? 'Large fixture task' : 'Fixture task'} ${number}
track: ${track}
milestone: ${milestone}
${dependency}createdAt: ${timestamp}
modifiedAt: ${timestamp}
---

Fixture description for ${id}.${longDescription}
`,
    );
    await writeFile(join(pm, 'states', state, `${id}.ref`), `../../tasks/${id}.md`);
  }

  const wikiCount = size === 'large' ? 48 : 4;
  for (let number = 1; number <= wikiCount; number += 1) {
    const relative =
      number === 1 ? 'welcome.md' : `guides/section-${Math.ceil(number / 6)}/page-${number}.md`;
    const path = join(pm, 'wiki', relative);
    await mkdir(join(path, '..'), { recursive: true });
    await writeFile(
      path,
      `---
title: Wiki page ${number}
createdAt: ${timestamp}
modifiedAt: ${timestamp}
---

# Wiki page ${number}

Local fixture content.${
        process.env.PM_E2E_MODE === 'static' && number === 1
          ? '\n\n[Current task](pm://project/playwright-project/task/E2E-0001)\n\n[Published wiki](pm://project/published-project/wiki/guide/hello%20world)\n\n[Unavailable wiki](pm://project/unavailable-project/wiki/guide/missing)'
          : ''
      }
`,
    );
  }
}

async function createLinkedFixtures(pm) {
  const linkedPm = join(linkedProjectRoot, '.pm');
  const siblingPm = join(siblingProjectRoot, '.pm');
  await Promise.all([
    mkdir(join(linkedPm, 'tasks'), { recursive: true }),
    mkdir(join(linkedPm, 'states', 'todo'), { recursive: true }),
    mkdir(join(linkedPm, 'states', 'done'), { recursive: true }),
    mkdir(join(linkedPm, 'wiki'), { recursive: true }),
    mkdir(join(siblingPm, 'tasks'), { recursive: true }),
    mkdir(join(siblingPm, 'states', 'todo'), { recursive: true }),
    mkdir(join(siblingPm, 'states', 'done'), { recursive: true }),
    mkdir(join(siblingPm, 'wiki'), { recursive: true }),
  ]);
  await writeFile(
    join(pm, 'linked_projects.yaml'),
    `version: 1
children:
  - projectId: linked-project
    alias: linked
    repositoryUrl: https://example.test/linked.git
    pathHint: linked
  - projectId: sibling-project
    alias: sibling
    repositoryUrl: https://example.test/sibling.git
    pathHint: sibling
  - projectId: unavailable-project
    alias: unavailable
    repositoryUrl: https://example.test/unavailable.git
    pathHint: missing-unavailable
`,
  );
  await writeFile(
    join(linkedPm, 'linked_projects.yaml'),
    `version: 1
parent:
  projectId: playwright-project
  alias: parent
  repositoryUrl: https://example.test/playwright.git
  pathHint: ..
`,
  );
  await writeFile(
    join(linkedPm, 'pm_config.yaml'),
    `name: Royale Project
idWidth: 4
idPrefix: LINK
nextIdServiceUrl: http://127.0.0.1:${idPort}
taskStates:
  todo: To Do
  done: Done
tracks:
  LINK: Linked work
milestones:
  shared:
    title: Shared Release
    description: ''
    priority: none
    requiredActivationTriggers: []
    delivery: null
`,
  );
  await writeFile(join(linkedPm, 'project_id.txt'), 'linked-project\n');
  await writeFile(
    join(linkedPm, 'tasks', 'LINK-0001.md'),
    `---
id: LINK-0001
title: Linked fixture task
track: LINK
milestone: shared
createdAt: ${timestamp}
modifiedAt: ${timestamp}
---

Read-only linked task body.
`,
  );
  await writeFile(join(linkedPm, 'states', 'todo', 'LINK-0001.ref'), '../../tasks/LINK-0001.md');
  await writeFile(
    join(linkedPm, 'wiki', 'linked-guide.md'),
    `---
title: Linked guide
createdAt: ${timestamp}
modifiedAt: ${timestamp}
---

# Linked guide

Read-only linked wiki body.
`,
  );

  await writeFile(
    join(siblingPm, 'linked_projects.yaml'),
    `version: 1
parent:
  projectId: playwright-project
  alias: parent
  repositoryUrl: https://example.test/playwright.git
  pathHint: ..
`,
  );
  await writeFile(
    join(siblingPm, 'pm_config.yaml'),
    `name: Starfall Project
idWidth: 4
idPrefix: STAR
nextIdServiceUrl: http://127.0.0.1:${idPort}
taskStates:
  todo: To Do
  done: Done
tracks:
  STAR: Starfall work
milestones:
  sibling:
    title: Sibling Release
    description: ''
    priority: none
    requiredActivationTriggers: []
    delivery: null
`,
  );
  await writeFile(join(siblingPm, 'project_id.txt'), 'sibling-project\n');
  await writeFile(
    join(siblingPm, 'tasks', 'STAR-0001.md'),
    `---
id: STAR-0001
title: Starfall fixture task
track: STAR
milestone: sibling
dependsOn:
- pm://project/playwright-project/task/E2E-0001
createdAt: ${timestamp}
modifiedAt: ${timestamp}
---

Read-only sibling task body.
`,
  );
  await writeFile(join(siblingPm, 'states', 'todo', 'STAR-0001.ref'), '../../tasks/STAR-0001.md');
  await writeFile(
    join(siblingPm, 'wiki', 'starfall-guide.md'),
    `---
title: Starfall guide
createdAt: ${timestamp}
modifiedAt: ${timestamp}
---

# Starfall guide

Read-only sibling wiki body.
`,
  );
}
