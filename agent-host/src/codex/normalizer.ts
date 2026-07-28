import { isAbsolute, relative, resolve } from 'node:path';
import type { ThreadEvent, ThreadItem } from '@openai/codex-sdk';
import type { AgentDriverEvent } from '../drivers.js';
import { sanitizeEventDraft } from '../protocol/event-sanitizer.js';

const commandOutputChunkLength = 12_000;

export interface NormalizedCodexEvent {
  readonly events: readonly AgentDriverEvent[];
  readonly fatal: boolean;
}

export class CodexEventNormalizer {
  private readonly commandOutputLengths = new Map<string, number>();

  constructor(private readonly workspaceDirectory: string) {}

  normalize(event: ThreadEvent): NormalizedCodexEvent {
    switch (event.type) {
      case 'thread.started':
        return result([
          this.event(
            'agent.thread_started',
            'Codex thread started',
            { threadId: event.thread_id },
            event.thread_id,
          ),
        ]);
      case 'turn.started':
        return result([this.event('agent.turn_started', 'Codex turn started')]);
      case 'turn.completed':
        return result([
          this.event('agent.turn_completed', 'Codex turn completed', { usage: event.usage }),
        ]);
      case 'turn.failed':
        return result(
          [
            this.event('agent.turn_failed', 'Codex turn failed', {
              message: event.error.message,
            }),
          ],
          true,
        );
      case 'error':
        return result(
          [this.event('agent.error', 'Codex stream failed', { message: event.message })],
          true,
        );
      case 'item.started':
      case 'item.updated':
      case 'item.completed':
        return this.normalizeItem(event.type, event.item);
    }
  }

  private normalizeItem(
    phase: 'item.started' | 'item.updated' | 'item.completed',
    item: ThreadItem,
  ): NormalizedCodexEvent {
    switch (item.type) {
      case 'command_execution':
        return result(this.normalizeCommand(phase, item));
      case 'file_change':
        if (phase !== 'item.completed') return result([]);
        return result([
          this.event('agent.file_change', 'Workspace files changed', {
            itemId: item.id,
            status: item.status,
            changes: item.changes.map((change) => ({
              path: this.workspacePath(change.path),
              kind: change.kind,
            })),
          }),
        ]);
      case 'mcp_tool_call':
        if (phase === 'item.updated') return result([]);
        return result([
          this.event(
            phase === 'item.started' ? 'mcp.tool_started' : 'mcp.tool_completed',
            phase === 'item.started'
              ? `PM MCP tool ${item.tool} started`
              : `PM MCP tool ${item.tool} ${item.status}`,
            {
              itemId: item.id,
              server: item.server,
              tool: item.tool,
              status: item.status,
              ...(item.status === 'failed' && item.error !== undefined
                ? { error: item.error.message }
                : {}),
            },
          ),
        ]);
      case 'agent_message':
        return phase === 'item.completed'
          ? result([
              this.event('agent.message', 'Codex response', {
                itemId: item.id,
                text: item.text,
              }),
            ])
          : result([]);
      case 'reasoning':
        return phase === 'item.completed'
          ? result([
              this.event('agent.reasoning', 'Codex reasoning summary', {
                itemId: item.id,
                text: item.text,
              }),
            ])
          : result([]);
      case 'todo_list':
        return result([
          this.event('agent.plan_updated', 'Codex plan updated', {
            itemId: item.id,
            items: item.items,
          }),
        ]);
      case 'web_search':
        return result([
          this.event('agent.web_search', 'Codex web search', {
            itemId: item.id,
            query: item.query,
            phase,
          }),
        ]);
      case 'error':
        return phase === 'item.completed'
          ? result([
              this.event('agent.item_error', 'Codex reported an error', {
                itemId: item.id,
                message: item.message,
              }),
            ])
          : result([]);
    }
  }

  private normalizeCommand(
    phase: 'item.started' | 'item.updated' | 'item.completed',
    item: Extract<ThreadItem, { type: 'command_execution' }>,
  ): AgentDriverEvent[] {
    const events: AgentDriverEvent[] = [];
    if (phase === 'item.started')
      events.push(
        this.event('command.started', 'Command started', {
          itemId: item.id,
          command: item.command,
          status: item.status,
        }),
      );

    const previousLength = this.commandOutputLengths.get(item.id) ?? 0;
    const reset = item.aggregated_output.length < previousLength;
    const output = reset ? item.aggregated_output : item.aggregated_output.slice(previousLength);
    this.commandOutputLengths.set(item.id, item.aggregated_output.length);
    for (let index = 0; index < output.length; index += commandOutputChunkLength)
      events.push(
        this.event('command.output', 'Command output', {
          itemId: item.id,
          output: output.slice(index, index + commandOutputChunkLength),
          ...(reset && index === 0 ? { reset: true } : {}),
        }),
      );

    if (phase === 'item.completed') {
      this.commandOutputLengths.delete(item.id);
      events.push(
        this.event(
          'command.completed',
          item.status === 'failed' ? 'Command failed' : 'Command completed',
          {
            itemId: item.id,
            command: item.command,
            status: item.status,
            exitCode: item.exit_code ?? null,
          },
        ),
      );
    }
    return events;
  }

  private workspacePath(value: string): string {
    const absolute = isAbsolute(value) ? resolve(value) : resolve(this.workspaceDirectory, value);
    const relativePath = relative(this.workspaceDirectory, absolute);
    if (relativePath === '' || relativePath === '.') return '.';
    if (
      relativePath === '..' ||
      relativePath.startsWith(`..${process.platform === 'win32' ? '\\' : '/'}`)
    )
      return '[outside-workspace]';
    return relativePath.replaceAll('\\', '/');
  }

  private event(
    type: string,
    summary: string,
    data: unknown = null,
    agentThreadId?: string,
  ): AgentDriverEvent {
    const sanitized = sanitizeEventDraft(type, summary, data);
    return {
      type: sanitized.type,
      summary: sanitized.summary,
      data: sanitized.data,
      ...(agentThreadId === undefined ? {} : { agentThreadId }),
    };
  }
}

function result(events: readonly AgentDriverEvent[], fatal = false): NormalizedCodexEvent {
  return { events, fatal };
}
