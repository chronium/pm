import { lstatSync } from 'node:fs';
import { join } from 'node:path';
import {
  Codex,
  type CodexOptions,
  type ModelReasoningEffort,
  type ThreadEvent,
  type ThreadOptions,
  type TurnOptions,
} from '@openai/codex-sdk';
import type { AgentDriverEvent } from '../drivers.js';
import { buildTaskExecutionPrompt } from './prompt.js';
import type { CodexWorkerRequest } from './protocol.js';
import { parseCodexWorkerRequest } from './protocol.js';
import { CodexEventNormalizer } from './normalizer.js';

const supportedEfforts = new Set(['minimal', 'low', 'medium', 'high', 'xhigh']);

export interface CodexThreadLike {
  runStreamed(
    input: string,
    options?: TurnOptions,
  ): Promise<{ events: AsyncIterable<ThreadEvent> }>;
}

export interface CodexClientLike {
  startThread(options?: ThreadOptions): CodexThreadLike;
}

export interface CodexClientFactory {
  create(options: CodexOptions): CodexClientLike;
}

const defaultFactory: CodexClientFactory = {
  create: (options) => new Codex(options),
};

export class CodexWorkerError extends Error {
  constructor(
    readonly code: string,
    message: string,
  ) {
    super(message);
    this.name = 'CodexWorkerError';
  }
}

export async function* executeCodexWorker(
  input: unknown,
  signal: AbortSignal,
  environment: NodeJS.ProcessEnv = process.env,
  factory: CodexClientFactory = defaultFactory,
): AsyncIterable<AgentDriverEvent> {
  const request = parseCodexWorkerRequest(input);
  validateAgentSelection(request);
  const childEnvironment = selectEnvironment(request, environment);
  validateAuthentication(request.codexHomeDirectory);

  const options = createCodexOptions(request, childEnvironment);
  const threadOptions = createThreadOptions(request);
  const client = factory.create(options);
  const thread = client.startThread(threadOptions);
  const streamed = await thread.runStreamed(buildTaskExecutionPrompt(request.runRequest), {
    signal,
  });
  const normalizer = new CodexEventNormalizer(request.workspaceDirectory);
  for await (const sdkEvent of streamed.events) {
    const normalized = normalizer.normalize(sdkEvent);
    for (const event of normalized.events) yield event;
    if (normalized.fatal) throw new CodexWorkerError('codex_turn_failed', 'The Codex turn failed.');
  }
}

export function createCodexOptions(
  request: CodexWorkerRequest,
  environment: Record<string, string>,
): CodexOptions {
  return {
    env: environment,
    config: {
      allow_login_shell: false,
      shell_environment_policy: {
        include_only: [...request.environmentNames],
      },
      sandbox_workspace_write: {
        network_access: request.networkAccessEnabled,
      },
      mcp_servers: {
        pm: {
          command: request.pmMcpCommand.executable,
          args: [
            ...request.pmMcpCommand.arguments,
            'mcp',
            '--profile',
            'run-worker',
            '--task-id',
            request.runRequest.specification.task.taskId,
          ],
          cwd: request.workspaceDirectory,
          required: true,
          startup_timeout_sec: 10,
          tool_timeout_sec: 60,
          default_tools_approval_mode: 'approve',
        },
      },
    },
  };
}

export function createThreadOptions(request: CodexWorkerRequest): ThreadOptions {
  return {
    model: request.runRequest.specification.agent.modelId,
    modelReasoningEffort: request.runRequest.specification.agent.effortId as ModelReasoningEffort,
    approvalPolicy: 'never',
    sandboxMode: 'workspace-write',
    workingDirectory: request.workspaceDirectory,
    skipGitRepoCheck: false,
    networkAccessEnabled: request.networkAccessEnabled,
    webSearchMode: 'disabled',
  };
}

function validateAgentSelection(request: CodexWorkerRequest): void {
  const agent = request.runRequest.specification.agent;
  if (agent.providerId !== 'codex')
    throw new CodexWorkerError('unsupported_agent_provider', 'The agent provider is unsupported.');
  if (!supportedEfforts.has(agent.effortId))
    throw new CodexWorkerError(
      'unsupported_reasoning_effort',
      'The reasoning effort is unsupported.',
    );
  if (agent.promptProfileId !== 'task-execution')
    throw new CodexWorkerError('unsupported_prompt_profile', 'The prompt profile is unsupported.');
}

function selectEnvironment(
  request: CodexWorkerRequest,
  source: NodeJS.ProcessEnv,
): Record<string, string> {
  const result: Record<string, string> = {};
  for (const name of request.environmentNames) {
    const value = source[name];
    if (value === undefined)
      throw new CodexWorkerError(
        'missing_runtime_environment',
        `Required runtime environment variable ${name} is unavailable.`,
      );
    result[name] = value;
  }
  if (result['CODEX_HOME'] !== request.codexHomeDirectory)
    throw new CodexWorkerError(
      'invalid_codex_home',
      'CODEX_HOME does not match the isolated runtime directory.',
    );
  return result;
}

function validateAuthentication(codexHomeDirectory: string): void {
  const authenticationPath = join(codexHomeDirectory, 'auth.json');
  let stats;
  try {
    stats = lstatSync(authenticationPath);
  } catch {
    throw new CodexWorkerError(
      'missing_codex_authentication',
      'The isolated Codex authentication snapshot is unavailable.',
    );
  }
  if (stats.isSymbolicLink() || !stats.isFile() || (stats.mode & 0o077) !== 0)
    throw new CodexWorkerError(
      'insecure_codex_authentication',
      'The isolated Codex authentication snapshot is not an owner-only regular file.',
    );
}
