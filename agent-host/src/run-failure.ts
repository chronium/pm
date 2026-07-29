import type { RunFailure, RunFailureCode, RunFailureStage } from './protocol/types.js';

const catalog: Record<RunFailureCode, Omit<RunFailure, 'code'>> = {
  repository_not_allowed: {
    stage: 'workspace',
    summary: 'Repository policy rejected the run.',
    recommendedAction:
      'Add the exact repository remote to the runner allowlist, then launch a new run.',
    retryable: false,
  },
  repository_fetch_failed: {
    stage: 'workspace',
    summary: 'The runner could not fetch the repository.',
    recommendedAction:
      'Check runner network access and repository credentials, then launch a new run.',
    retryable: true,
  },
  base_revision_unavailable: {
    stage: 'workspace',
    summary: 'The requested base revision is unavailable on the remote.',
    recommendedAction: 'Push the base revision to the configured remote, then launch a new run.',
    retryable: false,
  },
  task_revision_mismatch: {
    stage: 'workspace',
    summary: 'The task revision does not match the immutable run request.',
    recommendedAction: 'Refresh the task and launch a new run from its current revision.',
    retryable: false,
  },
  workspace_policy_unsupported: {
    stage: 'workspace',
    summary: 'The repository uses a feature the runner does not support.',
    recommendedAction:
      'Remove the unsupported repository feature or use a compatible runner profile.',
    retryable: false,
  },
  workspace_preparation_failed: {
    stage: 'workspace',
    summary: 'Workspace preparation failed.',
    recommendedAction: 'Check runner storage and configuration, then launch a new run.',
    retryable: true,
  },
  runtime_start_failed: {
    stage: 'runtime',
    summary: 'The runtime could not be started.',
    recommendedAction:
      'Check the runtime profile, image, and Podman service, then launch a new run.',
    retryable: true,
  },
  runtime_resource_limit: {
    stage: 'runtime',
    summary: 'The runtime exceeded an enforced resource limit.',
    recommendedAction:
      'Review the runner capacity and runtime profile limits before launching a new run.',
    retryable: true,
  },
  runtime_timeout: {
    stage: 'runtime',
    summary: 'The runtime exceeded its time limit.',
    recommendedAction:
      'Review the run output and increase the runtime timeout only if the task needs it.',
    retryable: true,
  },
  runtime_cleanup_failed: {
    stage: 'runtime',
    summary: 'The runner could not clean up the runtime.',
    recommendedAction: 'Inspect the runner runtime inventory before launching another run.',
    retryable: true,
  },
  agent_start_failed: {
    stage: 'agent',
    summary: 'Codex could not be started.',
    recommendedAction:
      'Check the provider configuration and Codex authentication, then launch a new run.',
    retryable: true,
  },
  agent_execution_failed: {
    stage: 'agent',
    summary: 'Codex execution failed.',
    recommendedAction:
      'Review the preceding agent output and launch a new run after resolving the reported issue.',
    retryable: true,
  },
  validation_failed: {
    stage: 'validation',
    summary: 'Run validation failed.',
    recommendedAction:
      'Review the failed validation step and collected patch before deciding whether to retry.',
    retryable: false,
  },
  validation_timeout: {
    stage: 'validation',
    summary: 'Run validation timed out.',
    recommendedAction: 'Review the validation command and timeout before launching a new run.',
    retryable: true,
  },
  artifact_collection_failed: {
    stage: 'artifacts',
    summary: 'Artifact collection failed.',
    recommendedAction: 'Check runner storage and artifact limits before launching a new run.',
    retryable: true,
  },
  artifact_collection_timeout: {
    stage: 'artifacts',
    summary: 'Artifact collection timed out.',
    recommendedAction:
      'Check runner storage performance and patch size before launching a new run.',
    retryable: true,
  },
  run_cancelled: {
    stage: 'cancellation',
    summary: 'Run cancelled.',
    recommendedAction: 'Launch a new run when execution should resume.',
    retryable: true,
  },
  runner_restarted: {
    stage: 'system',
    summary: 'The runner restarted before the run completed.',
    recommendedAction: 'Confirm the runner is healthy, then launch a new run.',
    retryable: true,
  },
  internal_failure: {
    stage: 'system',
    summary: 'The runner encountered an internal failure.',
    recommendedAction: 'Retry once, then inspect private runner logs if the failure repeats.',
    retryable: true,
  },
};

export class RunFailureError extends Error {
  constructor(readonly failure: RunFailure) {
    super(failure.summary);
    this.name = 'RunFailureError';
  }
}

export function runFailure(code: RunFailureCode): RunFailure {
  return { code, ...catalog[code] };
}

export function failRun(code: RunFailureCode): RunFailureError {
  return new RunFailureError(runFailure(code));
}

export function classifyRunFailure(
  error: unknown,
  stage: RunFailureStage,
  cancellationRequested = false,
  abortReason?: unknown,
): RunFailure {
  if (error instanceof RunFailureError) return error.failure;
  if (cancellationRequested || isAbortError(error)) {
    if (abortReason === 'runtime_timeout') return runFailure('runtime_timeout');
    if (abortReason === 'collection_timeout') return runFailure('artifact_collection_timeout');
    return runFailure('run_cancelled');
  }
  if (isResourceLimit(error)) return runFailure('runtime_resource_limit');
  switch (stage) {
    case 'workspace':
      return runFailure('workspace_preparation_failed');
    case 'runtime':
      return runFailure('runtime_start_failed');
    case 'agent':
      return runFailure('agent_execution_failed');
    case 'validation':
      return runFailure('validation_failed');
    case 'artifacts':
      return runFailure('artifact_collection_failed');
    case 'cancellation':
      return runFailure('run_cancelled');
    case 'system':
      return runFailure('internal_failure');
  }
}

function isAbortError(error: unknown): boolean {
  return error instanceof Error && error.name === 'AbortError';
}

function isResourceLimit(error: unknown): boolean {
  if (error === null || typeof error !== 'object') return false;
  const code = (error as { code?: unknown }).code;
  return code === 'runtime_disk_limit_exceeded' || code === 'runner_disk_reserve_reached';
}
