import assert from 'node:assert/strict';
import { chmodSync, mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';
import type { CodexOptions, ThreadEvent, ThreadOptions, TurnOptions } from '@openai/codex-sdk';
import { CodexAgentDriver } from '../src/codex/agent-driver.js';
import { CodexEventNormalizer } from '../src/codex/normalizer.js';
import { buildTaskExecutionPrompt } from '../src/codex/prompt.js';
import {
  codexWorkerProtocolVersion,
  type CodexWorkerMessage,
  type CodexWorkerRequest,
} from '../src/codex/protocol.js';
import { executeCodexWorker, type CodexClientFactory } from '../src/codex/worker.js';
import type {
  AgentDriverEvent,
  RuntimeHandle,
  RuntimeProcessEvent,
  RuntimeProcessExecutor,
  RuntimeProcessRequest,
} from '../src/drivers.js';
import { createRequest, createTempDirectory } from './helpers.js';

test('task execution prompt preserves immutable context and authority boundaries', () => {
  const request = createRequest('run-prompt');
  const prompt = buildTaskExecutionPrompt(request);

  assert.match(prompt, /PM-0001 - Define the agent protocol/);
  assert.match(prompt, new RegExp(request.specificationHash));
  assert.match(prompt, /Base commit: a{40}/);
  assert.match(prompt, /Use the required PM MCP server/);
  assert.match(prompt, /Do not commit, create branches, push, merge/);
  assert.match(prompt, /PM remains authoritative for completion/);
  assert.match(prompt, /Build: dotnet build --no-restore/);
});

test('Codex worker config requires scoped PM MCP and unattended workspace-write', async () => {
  const temporary = createTempDirectory();
  try {
    const request = workerRequest(temporary.path);
    const captured: {
      options?: CodexOptions;
      thread?: ThreadOptions;
      prompt?: string;
      signal?: AbortSignal;
    } = {};
    const sdkEvents: ThreadEvent[] = [
      { type: 'thread.started', thread_id: 'thread-test' },
      { type: 'turn.started' },
      {
        type: 'item.completed',
        item: { id: 'message-1', type: 'agent_message', text: 'Implementation complete.' },
      },
      {
        type: 'turn.completed',
        usage: {
          input_tokens: 12,
          cached_input_tokens: 3,
          cache_write_input_tokens: 0,
          output_tokens: 4,
          reasoning_output_tokens: 2,
        },
      },
    ];
    const factory: CodexClientFactory = {
      create(options) {
        captured.options = options;
        return {
          startThread(optionsValue) {
            if (optionsValue !== undefined) captured.thread = optionsValue;
            return {
              async runStreamed(prompt: string, options?: TurnOptions) {
                captured.prompt = prompt;
                if (options?.signal !== undefined) captured.signal = options.signal;
                return { events: events(sdkEvents) };
              },
            };
          },
        };
      },
    };

    const output = [];
    const signal = new AbortController().signal;
    for await (const event of executeCodexWorker(
      request,
      signal,
      { CODEX_HOME: request.codexHomeDirectory, PATH: '/usr/bin' },
      factory,
    ))
      output.push(event);

    assert.equal(captured.thread?.approvalPolicy, 'never');
    assert.equal(captured.thread?.sandboxMode, 'workspace-write');
    assert.equal(captured.thread?.workingDirectory, request.workspaceDirectory);
    assert.equal(captured.thread?.model, request.runRequest.specification.agent.modelId);
    assert.equal(captured.thread?.modelReasoningEffort, 'medium');
    assert.equal(captured.thread?.webSearchMode, 'disabled');
    assert.equal(captured.signal, signal);
    assert.match(captured.prompt ?? '', /PM-0001/);
    assert.deepEqual(captured.options?.env, {
      CODEX_HOME: request.codexHomeDirectory,
      PATH: '/usr/bin',
    });
    assert.deepEqual(captured.options?.config?.['mcp_servers'], {
      pm: {
        command: 'dotnet',
        args: ['/opt/pm/PM.dll', 'mcp', '--profile', 'run-worker', '--task-id', 'PM-0001'],
        cwd: request.workspaceDirectory,
        required: true,
        startup_timeout_sec: 10,
        tool_timeout_sec: 60,
        default_tools_approval_mode: 'approve',
      },
    });
    assert.deepEqual(
      output.map((event) => event.type),
      ['agent.thread_started', 'agent.turn_started', 'agent.message', 'agent.turn_completed'],
    );
    assert.equal(output[0]?.agentThreadId, 'thread-test');
    assert.deepEqual(output.at(-1)?.data, {
      usage: {
        input_tokens: 12,
        cached_input_tokens: 3,
        cache_write_input_tokens: 0,
        output_tokens: 4,
        reasoning_output_tokens: 2,
      },
    });
  } finally {
    temporary.dispose();
  }
});

test('Codex worker rejects missing authentication before creating the SDK client', async () => {
  const temporary = createTempDirectory();
  try {
    const request = workerRequest(temporary.path, false);
    let created = false;
    const factory: CodexClientFactory = {
      create() {
        created = true;
        throw new Error('should not be reached');
      },
    };
    await assert.rejects(
      async () => {
        for await (const _ of executeCodexWorker(
          request,
          new AbortController().signal,
          { CODEX_HOME: request.codexHomeDirectory, PATH: '/usr/bin' },
          factory,
        )) {
          // No events are expected.
        }
      },
      { name: 'CodexWorkerError', code: 'missing_codex_authentication' },
    );
    assert.equal(created, false);
  } finally {
    temporary.dispose();
  }
});

test('required MCP startup failure is streamed and remains fatal', async () => {
  const temporary = createTempDirectory();
  try {
    const request = workerRequest(temporary.path);
    const factory: CodexClientFactory = {
      create() {
        return {
          startThread() {
            return {
              async runStreamed() {
                return {
                  events: events([
                    { type: 'thread.started', thread_id: 'thread-mcp-failure' },
                    {
                      type: 'error',
                      message: 'Required MCP server pm failed to initialize.',
                    },
                  ]),
                };
              },
            };
          },
        };
      },
    };
    const output: AgentDriverEvent[] = [];
    await assert.rejects(
      async () => {
        for await (const event of executeCodexWorker(
          request,
          new AbortController().signal,
          { CODEX_HOME: request.codexHomeDirectory, PATH: '/usr/bin' },
          factory,
        ))
          output.push(event);
      },
      { name: 'CodexWorkerError', code: 'codex_turn_failed' },
    );
    assert.deepEqual(
      output.map((event) => event.type),
      ['agent.thread_started', 'agent.error'],
    );
  } finally {
    temporary.dispose();
  }
});

test('event normalizer emits command deltas, redacts secrets, and hides outside paths', () => {
  const normalizer = new CodexEventNormalizer('/workspace');
  const started = normalizer.normalize({
    type: 'item.started',
    item: {
      id: 'command-1',
      type: 'command_execution',
      command: 'curl -H "Authorization: Bearer secret_token_123456"',
      aggregated_output: 'first\n',
      status: 'in_progress',
    },
  });
  const completed = normalizer.normalize({
    type: 'item.completed',
    item: {
      id: 'command-1',
      type: 'command_execution',
      command: 'curl -H "Authorization: Bearer secret_token_123456"',
      aggregated_output: 'first\nsecond\n',
      exit_code: 0,
      status: 'completed',
    },
  });
  const changed = normalizer.normalize({
    type: 'item.completed',
    item: {
      id: 'change-1',
      type: 'file_change',
      changes: [
        { path: '/workspace/src/file.ts', kind: 'update' },
        { path: '/etc/passwd', kind: 'update' },
      ],
      status: 'completed',
    },
  });

  assert.deepEqual(
    started.events.map((event) => event.type),
    ['command.started', 'command.output'],
  );
  assert.equal(
    (completed.events.find((event) => event.type === 'command.output')?.data as { output: string })
      .output,
    'second\n',
  );
  assert.doesNotMatch(JSON.stringify([...started.events, ...completed.events]), /secret_token/);
  assert.deepEqual((changed.events[0]?.data as { changes: unknown }).changes, [
    { path: 'src/file.ts', kind: 'update' },
    { path: '[outside-workspace]', kind: 'update' },
  ]);
});

test('event normalizer omits MCP arguments and results', () => {
  const normalizer = new CodexEventNormalizer('/workspace');
  const normalized = normalizer.normalize({
    type: 'item.completed',
    item: {
      id: 'mcp-1',
      type: 'mcp_tool_call',
      server: 'pm',
      tool: 'get_task',
      arguments: { token: 'secret', taskId: 'PM-0001' },
      result: { content: [], structured_content: { body: 'private detail' } },
      status: 'completed',
    },
  });

  const serialized = JSON.stringify(normalized.events);
  assert.match(serialized, /get_task/);
  assert.doesNotMatch(serialized, /private detail|taskId|secret/);
});

test('agent driver parses bounded JSONL and passes no environment values through stdin', async () => {
  const request = createRequest('run-driver');
  const runtime = runtimeHandle();
  let invocation: RuntimeProcessRequest | undefined;
  const messages: CodexWorkerMessage[] = [
    {
      protocolVersion: codexWorkerProtocolVersion,
      type: 'event',
      event: {
        type: 'agent.thread_started',
        summary: 'Codex thread started',
        data: { threadId: 'thread-driver' },
        agentThreadId: 'thread-driver',
      },
    },
    { protocolVersion: codexWorkerProtocolVersion, type: 'completed' },
  ];
  const executor: RuntimeProcessExecutor = {
    async *execute(_runtime, processRequest): AsyncIterable<RuntimeProcessEvent> {
      invocation = processRequest;
      const output = messages.map((message) => JSON.stringify(message)).join('\n') + '\n';
      yield { type: 'stdout', chunk: output.slice(0, 17) };
      yield { type: 'stdout', chunk: output.slice(17) };
      yield { type: 'exit', exitCode: 0, signal: null };
    },
  };
  const output: AgentDriverEvent[] = [];
  for await (const event of new CodexAgentDriver(executor).execute(
    request,
    runtime,
    new AbortController().signal,
  ))
    output.push(event);

  assert.equal(output[0]?.agentThreadId, 'thread-driver');
  assert.equal(invocation?.command, runtime.agentContext.workerCommand);
  assert.equal(invocation?.environment, runtime.agentContext.environment);
  assert.doesNotMatch(invocation?.standardInput ?? '', /\/usr\/local\/secret-value/);
  assert.match(invocation?.standardInput ?? '', /"environmentNames"/);
});

test('agent driver surfaces a sanitized worker failure and rejects the run', async () => {
  const executor: RuntimeProcessExecutor = {
    async *execute(): AsyncIterable<RuntimeProcessEvent> {
      yield {
        type: 'stdout',
        chunk: `${JSON.stringify({
          protocolVersion: codexWorkerProtocolVersion,
          type: 'failed',
          errorCode: 'codex_turn_failed',
          message: 'Bearer abcdefghijklmnopqrstuvwxyz',
        })}\n`,
      };
      yield { type: 'exit', exitCode: 1, signal: null };
    },
  };
  const output: AgentDriverEvent[] = [];
  await assert.rejects(async () => {
    for await (const event of new CodexAgentDriver(executor).execute(
      createRequest('run-failed-driver'),
      runtimeHandle(),
      new AbortController().signal,
    ))
      output.push(event);
  });
  assert.equal(output[0]?.type, 'agent.worker_failed');
  assert.doesNotMatch(JSON.stringify(output), /abcdefghijkl/);
});

test('agent driver rejects malformed worker output', async () => {
  const executor: RuntimeProcessExecutor = {
    async *execute(): AsyncIterable<RuntimeProcessEvent> {
      yield { type: 'stdout', chunk: '{not-json}\n' };
      yield { type: 'exit', exitCode: 0, signal: null };
    },
  };
  await assert.rejects(async () => {
    for await (const _ of new CodexAgentDriver(executor).execute(
      createRequest('run-malformed-driver'),
      runtimeHandle(),
      new AbortController().signal,
    )) {
      // No events are expected.
    }
  }, /invalid JSONL/);
});

test('agent driver rejects oversized worker output lines', async () => {
  const executor: RuntimeProcessExecutor = {
    async *execute(): AsyncIterable<RuntimeProcessEvent> {
      yield { type: 'stdout', chunk: 'x'.repeat(1_048_577) };
    },
  };
  await assert.rejects(async () => {
    for await (const _ of new CodexAgentDriver(executor).execute(
      createRequest('run-oversized-driver'),
      runtimeHandle(),
      new AbortController().signal,
    )) {
      // No events are expected.
    }
  }, /too large/);
});

test('agent driver propagates cancellation to the runtime executor', async () => {
  const controller = new AbortController();
  let observedSignal: AbortSignal | undefined;
  const executor: RuntimeProcessExecutor = {
    async *execute(_runtime, _request, signal): AsyncIterable<RuntimeProcessEvent> {
      observedSignal = signal;
      controller.abort('client_requested');
      yield { type: 'exit', exitCode: null, signal: 'SIGTERM' };
    },
  };
  await assert.rejects(
    async () => {
      for await (const _ of new CodexAgentDriver(executor).execute(
        createRequest('run-cancel-driver'),
        runtimeHandle(),
        controller.signal,
      )) {
        // No events are expected.
      }
    },
    { name: 'AbortError' },
  );
  assert.equal(observedSignal, controller.signal);
});

function workerRequest(root: string, withAuthentication = true): CodexWorkerRequest {
  const workspaceDirectory = join(root, 'workspace');
  const codexHomeDirectory = join(root, 'codex-home');
  mkdirSync(workspaceDirectory, { recursive: true });
  mkdirSync(codexHomeDirectory, { recursive: true, mode: 0o700 });
  if (withAuthentication) {
    const authenticationPath = join(codexHomeDirectory, 'auth.json');
    writeFileSync(authenticationPath, '{}', { mode: 0o600 });
    chmodSync(authenticationPath, 0o600);
  }
  return {
    protocolVersion: codexWorkerProtocolVersion,
    runRequest: createRequest('run-worker'),
    workspaceDirectory,
    codexHomeDirectory,
    networkAccessEnabled: false,
    pmMcpCommand: { executable: 'dotnet', arguments: ['/opt/pm/PM.dll'] },
    environmentNames: ['CODEX_HOME', 'PATH'],
  };
}

function runtimeHandle(): RuntimeHandle {
  return {
    runtimeId: 'runtime-1',
    agentContext: {
      workspaceDirectory: '/workspace',
      codexHomeDirectory: '/run/codex-home',
      networkAccessEnabled: false,
      workerCommand: { executable: 'node', arguments: ['/opt/pm-agent/worker.js'] },
      pmMcpCommand: { executable: 'pm', arguments: [] },
      environment: {
        CODEX_HOME: '/run/codex-home',
        PATH: '/usr/local/secret-value',
      },
    },
  };
}

async function* events(values: readonly ThreadEvent[]): AsyncIterable<ThreadEvent> {
  for (const value of values) yield value;
}
