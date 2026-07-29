import { Injectable } from '@angular/core';

import type { AgentRunEvent, AgentRunState } from './agent-runs-api.service';

export interface AgentRunStreamEnd {
  state: AgentRunState;
  lastSequence: number | string;
}

export interface AgentRunStreamHandlers {
  open(): void;
  event(event: AgentRunEvent): void;
  end(end: AgentRunStreamEnd): void;
  error(): void;
}

export interface AgentRunStreamConnection {
  close(): void;
}

@Injectable({ providedIn: 'root' })
export class AgentRunEventStreamService {
  connect(
    runId: string,
    afterSequence: number,
    handlers: AgentRunStreamHandlers,
  ): AgentRunStreamConnection {
    const source = new EventSource(
      `/api/v1/runs/${encodeURIComponent(runId)}/events/stream?afterSequence=${afterSequence}`,
    );
    source.addEventListener('open', () => handlers.open());
    source.addEventListener('run-event', (message) => {
      try {
        handlers.event(JSON.parse((message as MessageEvent<string>).data) as AgentRunEvent);
      } catch {
        handlers.error();
        source.close();
      }
    });
    source.addEventListener('stream-end', (message) => {
      try {
        handlers.end(JSON.parse((message as MessageEvent<string>).data) as AgentRunStreamEnd);
      } catch {
        handlers.error();
      } finally {
        source.close();
      }
    });
    source.addEventListener('error', () => handlers.error());
    return source;
  }
}
