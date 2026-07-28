import type { RunRequest } from '../protocol/types.js';

export function buildTaskExecutionPrompt(request: RunRequest): string {
  const { specification, specificationHash } = request;
  const validation = specification.runtime.profile.validation
    .map(
      (step) =>
        `- ${step.displayName}: ${formatCommand(step.executable, step.arguments)} (from ${step.workingDirectory})`,
    )
    .join('\n');

  return `You are executing one bounded PM task in an isolated runner workspace.

Run identity:
- Run: ${specification.runId}
- Specification: ${specificationHash}
- Project: ${specification.project.name} (${specification.project.projectId})
- Task: ${specification.task.taskId} - ${specification.task.title}
- Task revision: ${specification.task.revision}
- Base commit: ${specification.repository.baseCommit}

Required workflow:
1. Use the required PM MCP server to read ${specification.task.taskId}, its dependencies, and relevant wiki context before editing.
2. Follow repository AGENTS.md instructions and keep work limited to the assigned task.
3. Implement the task, add focused tests, and run the relevant validation.
4. Report changed files, validation evidence, remaining risks, and whether you believe the implementation is complete.
5. You may append an implementation note to ${specification.task.taskId} through PM MCP when useful.

Authority boundaries:
- Do not move, complete, remove, rename, or otherwise change authoritative PM task state or metadata.
- Do not create or modify unrelated tasks, wiki pages, milestones, tracks, statuses, project membership, or project configuration.
- Do not commit, create branches, push, merge, or access credentials.
- Your conclusion is advisory; PM remains authoritative for completion after validation or review.

Configured post-run validation:
${validation.length === 0 ? '- No post-run validation steps are configured.' : validation}
`;
}

function formatCommand(executable: string, argumentsValue: readonly string[]): string {
  return [executable, ...argumentsValue].map(shellQuote).join(' ');
}

function shellQuote(value: string): string {
  return /^[A-Za-z0-9_./:@%+=,-]+$/.test(value) ? value : JSON.stringify(value);
}
