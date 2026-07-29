import { DatePipe, TitleCasePipe } from '@angular/common';
import { Component, input, output } from '@angular/core';

import type { AgentRunArtifact, AgentRunInspection } from './agent-runs-api.service';
import type { AgentRunCheckpoint } from './agent-run-events';
import type { AgentArtifactDownloadState } from './agent-run-artifact-download';

@Component({
  selector: 'pm-agent-run-progress',
  imports: [DatePipe, TitleCasePipe],
  templateUrl: './agent-run-progress.html',
  styleUrl: './agent-run-progress.css',
})
export class AgentRunProgress {
  readonly inspection = input.required<AgentRunInspection>();
  readonly checkpoints = input.required<AgentRunCheckpoint[]>();
  readonly artifacts = input<AgentRunArtifact[]>([]);
  readonly artifactDownloads = input<Record<string, AgentArtifactDownloadState>>({});
  readonly downloadRequested = output<AgentRunArtifact>();
  readonly collectRequested = output<void>();

  protected downloadState(artifactId: string): AgentArtifactDownloadState {
    return this.artifactDownloads()[artifactId] ?? { status: 'idle', message: null };
  }

  protected formatBytes(value: number | string): string {
    const bytes = Number(value);
    if (!Number.isFinite(bytes) || bytes < 0) return String(value);
    const units = ['B', 'KiB', 'MiB', 'GiB'];
    let size = bytes;
    let unit = 0;
    while (size >= 1024 && unit < units.length - 1) {
      size /= 1024;
      unit += 1;
    }
    return `${size >= 10 || unit === 0 ? size.toFixed(0) : size.toFixed(1)} ${units[unit]}`;
  }
}
