import { mkdir, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';

export const e2eRoot = process.env.PM_E2E_ROOT;
if (!e2eRoot) throw new Error('PM_E2E_ROOT must be set by the Playwright configuration.');

export const projectRoot = join(e2eRoot, 'project');
export const configRoot = join(e2eRoot, 'config');
const idPort = process.env.PM_E2E_ID_PORT;
if (!idPort) throw new Error('PM_E2E_ID_PORT must be set by the E2E runner.');

const timestamp = '2026-01-01T00:00:00.0000000Z';

export async function resetFixture(size = 'small') {
  const pm = join(projectRoot, '.pm');
  await rm(pm, { recursive: true, force: true });
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
  current: Current Release
  later: Later
milestonePriorities:
  current: high
`,
  );
  await writeFile(join(pm, 'project_id.txt'), 'playwright-project\n');

  const count = size === 'large' ? 180 : 6;
  for (let number = 1; number <= count; number += 1) {
    const id = `E2E-${String(number).padStart(4, '0')}`;
    const state =
      size === 'large' && number <= 120 ? 'done' : number % 5 === 0 ? 'in-progress' : 'todo';
    const track = number % 3 === 0 ? 'OPS' : 'E2E';
    const milestone = number % 4 === 0 ? 'later' : 'current';
    const dependency =
      number > 1 && number % 6 === 0
        ? `dependsOn:\n- E2E-${String(number - 1).padStart(4, '0')}\n`
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

Local fixture content.
`,
    );
  }
}
