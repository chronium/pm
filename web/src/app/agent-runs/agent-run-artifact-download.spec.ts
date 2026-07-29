import { HttpHeaders, HttpResponse } from '@angular/common/http';

import { verifiedArtifactBlob } from './agent-run-artifact-download';
import type { AgentRunArtifact } from './agent-runs-api.service';

const bytes = new TextEncoder().encode('hello').buffer;
const sha256 = '2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824';
const artifact: AgentRunArtifact = {
  artifactId: 'changes-patch',
  kind: 'patch',
  fileName: 'changes.patch',
  mediaType: 'text/x-diff',
  byteLength: bytes.byteLength,
  sha256,
  createdAt: '2026-07-29T08:10:00.000Z',
};

function response(overrides: Record<string, string> = {}) {
  return new HttpResponse({
    body: bytes,
    headers: new HttpHeaders({
      'Content-Length': String(bytes.byteLength),
      'Content-Type': artifact.mediaType,
      'PM-Artifact-Id': artifact.artifactId,
      'PM-Artifact-SHA256': artifact.sha256,
      ETag: `"sha256:${artifact.sha256}"`,
      ...overrides,
    }),
  });
}

describe('verifiedArtifactBlob', () => {
  it('returns a blob only after metadata and content verification', async () => {
    const blob = await verifiedArtifactBlob(artifact, response());

    expect(blob.type).toBe('text/x-diff');
    expect(await blob.text()).toBe('hello');
  });

  it('rejects mismatched response metadata', async () => {
    await expect(
      verifiedArtifactBlob(artifact, response({ 'PM-Artifact-Id': 'other-artifact' })),
    ).rejects.toThrow('metadata did not match');
  });

  it('rejects content that does not match the retained digest', async () => {
    const wrongDigest = '00'.repeat(32);
    await expect(
      verifiedArtifactBlob(
        { ...artifact, sha256: wrongDigest },
        response({
          'PM-Artifact-SHA256': wrongDigest,
          ETag: `"sha256:${wrongDigest}"`,
        }),
      ),
    ).rejects.toThrow('integrity verification failed');
  });
});
