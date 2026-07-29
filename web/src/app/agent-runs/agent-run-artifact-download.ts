import type { HttpResponse } from '@angular/common/http';

import type { AgentRunArtifact } from './agent-runs-api.service';

export type AgentArtifactDownloadStatus = 'idle' | 'downloading' | 'downloaded' | 'error';

export interface AgentArtifactDownloadState {
  status: AgentArtifactDownloadStatus;
  message: string | null;
}

export async function verifiedArtifactBlob(
  artifact: AgentRunArtifact,
  response: HttpResponse<ArrayBuffer>,
): Promise<Blob> {
  const body = response.body;
  const contentLength = Number(response.headers.get('Content-Length'));
  const contentType = response.headers.get('Content-Type')?.split(';', 1)[0] ?? '';
  const artifactId = response.headers.get('PM-Artifact-Id');
  const digest = response.headers.get('PM-Artifact-SHA256');
  const etag = response.headers.get('ETag');
  if (
    !body ||
    !Number.isSafeInteger(contentLength) ||
    contentLength !== Number(artifact.byteLength) ||
    body.byteLength !== Number(artifact.byteLength) ||
    contentType !== artifact.mediaType ||
    artifactId !== artifact.artifactId ||
    digest !== artifact.sha256 ||
    etag !== `"sha256:${artifact.sha256}"`
  ) {
    throw new Error('Artifact response metadata did not match the retained artifact.');
  }

  const actual = bytesToHex(await globalThis.crypto.subtle.digest('SHA-256', body));
  if (actual !== artifact.sha256) throw new Error('Artifact integrity verification failed.');
  return new Blob([body], { type: artifact.mediaType });
}

function bytesToHex(value: ArrayBuffer): string {
  return [...new Uint8Array(value)].map((byte) => byte.toString(16).padStart(2, '0')).join('');
}
