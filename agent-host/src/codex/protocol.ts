import { isAbsolute } from 'node:path';
import type { AgentDriverEvent, RuntimeCommand } from '../drivers.js';
import type { RunRequest } from '../protocol/types.js';
import { parseRunRequest } from '../protocol/validation.js';

export const codexWorkerProtocolVersion = '1';

export interface CodexWorkerRequest {
  readonly protocolVersion: typeof codexWorkerProtocolVersion;
  readonly runRequest: RunRequest;
  readonly workspaceDirectory: string;
  readonly codexHomeDirectory: string;
  readonly networkAccessEnabled: boolean;
  readonly pmMcpCommand: RuntimeCommand;
  readonly environmentNames: readonly string[];
}

export type CodexWorkerMessage =
  | {
      readonly protocolVersion: typeof codexWorkerProtocolVersion;
      readonly type: 'event';
      readonly event: AgentDriverEvent;
    }
  | {
      readonly protocolVersion: typeof codexWorkerProtocolVersion;
      readonly type: 'completed';
    }
  | {
      readonly protocolVersion: typeof codexWorkerProtocolVersion;
      readonly type: 'failed';
      readonly errorCode: string;
      readonly message: string;
    };

const safeName = /^[A-Za-z_][A-Za-z0-9_]{0,127}$/;
const safeEventType = /^(?:agent|command|mcp)\.[a-z0-9][a-z0-9._-]*$/;
const safeErrorCode = /^[a-z][a-z0-9_]{0,127}$/;

export function parseCodexWorkerRequest(value: unknown): CodexWorkerRequest {
  const root = record(value, 'Codex worker request');
  if (root['protocolVersion'] !== codexWorkerProtocolVersion)
    throw new Error('Codex worker protocol version is unsupported.');

  const workspaceDirectory = absolutePath(root['workspaceDirectory'], 'Workspace directory');
  const codexHomeDirectory = absolutePath(root['codexHomeDirectory'], 'Codex home directory');
  const environmentNames = array(root['environmentNames'], 'Environment names').map((entry) => {
    const name = text(entry, 128, 'Environment name');
    if (!safeName.test(name)) throw new Error('Environment name is invalid.');
    return name;
  });
  if (environmentNames.length > 64 || new Set(environmentNames).size !== environmentNames.length)
    throw new Error('Environment names must be unique and contain at most 64 entries.');

  return {
    protocolVersion: codexWorkerProtocolVersion,
    runRequest: parseRunRequest(root['runRequest']),
    workspaceDirectory,
    codexHomeDirectory,
    networkAccessEnabled: boolean(root['networkAccessEnabled'], 'Network access'),
    pmMcpCommand: parseCommand(root['pmMcpCommand']),
    environmentNames,
  };
}

export function parseCodexWorkerMessage(value: unknown): CodexWorkerMessage {
  const root = record(value, 'Codex worker message');
  if (root['protocolVersion'] !== codexWorkerProtocolVersion)
    throw new Error('Codex worker protocol version is unsupported.');
  const type = root['type'];
  if (type === 'completed') return { protocolVersion: codexWorkerProtocolVersion, type };
  if (type === 'failed') {
    const errorCode = text(root['errorCode'], 128, 'Worker error code');
    if (!safeErrorCode.test(errorCode)) throw new Error('Worker error code is invalid.');
    return {
      protocolVersion: codexWorkerProtocolVersion,
      type,
      errorCode,
      message: text(root['message'], 4096, 'Worker failure message'),
    };
  }
  if (type !== 'event') throw new Error('Codex worker message type is invalid.');

  const event = record(root['event'], 'Codex worker event');
  const eventType = text(event['type'], 256, 'Event type');
  if (!safeEventType.test(eventType)) throw new Error('Codex worker event type is invalid.');
  const agentThreadId = optionalText(event['agentThreadId'], 256, 'Agent thread ID');
  return {
    protocolVersion: codexWorkerProtocolVersion,
    type,
    event: {
      type: eventType,
      summary: text(event['summary'], 4096, 'Event summary'),
      data: event['data'] ?? null,
      ...(agentThreadId === undefined ? {} : { agentThreadId }),
    },
  };
}

function parseCommand(value: unknown): RuntimeCommand {
  const command = record(value, 'Runtime command');
  const argumentsValue = array(command['arguments'], 'Runtime command arguments').map((entry) =>
    text(entry, 4096, 'Runtime command argument', true),
  );
  if (argumentsValue.length > 128) throw new Error('Runtime command has too many arguments.');
  return {
    executable: text(command['executable'], 1024, 'Runtime command executable'),
    arguments: argumentsValue,
  };
}

function record(value: unknown, name: string): Record<string, unknown> {
  if (value === null || typeof value !== 'object' || Array.isArray(value))
    throw new Error(`${name} must be an object.`);
  return value as Record<string, unknown>;
}

function array(value: unknown, name: string): unknown[] {
  if (!Array.isArray(value)) throw new Error(`${name} must be an array.`);
  return value;
}

function text(value: unknown, maximum: number, name: string, allowEmpty = false): string {
  if (
    typeof value !== 'string' ||
    (!allowEmpty && value.length === 0) ||
    value.length > maximum ||
    /[\u0000-\u001f\u007f]/.test(value)
  )
    throw new Error(`${name} is invalid.`);
  return value;
}

function optionalText(value: unknown, maximum: number, name: string): string | undefined {
  return value === undefined ? undefined : text(value, maximum, name);
}

function boolean(value: unknown, name: string): boolean {
  if (typeof value !== 'boolean') throw new Error(`${name} must be a boolean.`);
  return value;
}

function absolutePath(value: unknown, name: string): string {
  const path = text(value, 4096, name);
  if (!isAbsolute(path)) throw new Error(`${name} must be absolute.`);
  return path;
}
