import { constants, createReadStream } from 'node:fs';
import { open } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { pipeline } from 'node:stream/promises';
import type { ServerResponse } from 'node:http';
import type { RunStore } from './persistence/run-store.js';

export const maximumArtifactTransferBytes = 64 * 1024 * 1024;

export class ArtifactContentError extends Error {
  constructor(
    readonly status: 404 | 409 | 413,
    readonly errorCode: string,
    message: string,
  ) {
    super(message);
  }
}

export async function streamArtifactContent(
  store: RunStore,
  runId: string,
  artifactId: string,
  response: ServerResponse,
): Promise<void> {
  const stored = store.getStoredArtifact(runId, artifactId);
  if (stored === undefined)
    throw new ArtifactContentError(404, 'artifact_not_found', 'The artifact was not found.');
  if (stored.artifact.byteLength > maximumArtifactTransferBytes)
    throw new ArtifactContentError(
      413,
      'artifact_too_large',
      'The artifact exceeds the transfer limit.',
    );

  let file;
  try {
    file = await open(stored.absoluteLocation, constants.O_RDONLY | constants.O_NOFOLLOW);
  } catch {
    throw new ArtifactContentError(
      404,
      'artifact_unavailable',
      'The artifact is no longer available.',
    );
  }

  try {
    const stat = await file.stat();
    if (!stat.isFile())
      throw new ArtifactContentError(
        409,
        'artifact_invalid',
        'The artifact is not a regular file.',
      );
    if (stat.size !== stored.artifact.byteLength)
      throw new ArtifactContentError(
        409,
        'artifact_corrupt',
        'The artifact length does not match its metadata.',
      );

    const hash = createHash('sha256');
    const verification = createReadStream(stored.absoluteLocation, {
      fd: file.fd,
      autoClose: false,
      start: 0,
      end: Math.max(0, stat.size - 1),
    });
    if (stat.size > 0) for await (const chunk of verification) hash.update(chunk as Buffer);
    const digest = stat.size === 0 ? createHash('sha256').digest('hex') : hash.digest('hex');
    const afterVerification = await file.stat();
    if (afterVerification.size !== stat.size || digest !== stored.artifact.sha256)
      throw new ArtifactContentError(
        409,
        'artifact_corrupt',
        'The artifact digest does not match its metadata.',
      );

    response.statusCode = 200;
    response.setHeader('Content-Type', stored.artifact.mediaType);
    response.setHeader('Content-Length', String(stored.artifact.byteLength));
    response.setHeader('Content-Disposition', contentDisposition(stored.artifact.fileName));
    response.setHeader('Cache-Control', 'no-store');
    response.setHeader('X-Content-Type-Options', 'nosniff');
    response.setHeader('PM-Artifact-Id', stored.artifact.artifactId);
    response.setHeader('PM-Artifact-SHA256', stored.artifact.sha256);
    response.setHeader('ETag', `"sha256:${stored.artifact.sha256}"`);
    if (stat.size === 0) response.end();
    else
      await pipeline(
        createReadStream(stored.absoluteLocation, {
          fd: file.fd,
          autoClose: false,
          start: 0,
          end: stat.size - 1,
        }),
        response,
      );
  } finally {
    await file.close();
  }
}

function contentDisposition(fileName: string): string {
  const fallback = fileName.replaceAll(/[^A-Za-z0-9._-]/g, '_') || 'artifact';
  return `attachment; filename="${fallback}"; filename*=UTF-8''${encodeURIComponent(fileName)}`;
}
