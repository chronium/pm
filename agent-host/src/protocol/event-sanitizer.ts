const ansiSequence = /\u001b(?:\[[0-?]*[ -/]*[@-~]|\][^\u0007]*(?:\u0007|\u001b\\))/g;
const controlCharacter = /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g;
const sensitiveKey =
  /(?:authorization|cookie|credential|password|private.?key|secret|signature|nonce|token|api.?key)/i;
const privateKey =
  /-----BEGIN [^-\r\n]*PRIVATE KEY-----[\s\S]*?-----END [^-\r\n]*PRIVATE KEY-----/gi;
const bearerToken = /\bBearer\s+[A-Za-z0-9._~+/=-]{8,}/gi;
const commonToken = /\b(?:sk|ghp|github_pat|npm)_[A-Za-z0-9_-]{12,}\b/g;

const maximumDepth = 8;
const maximumCollectionEntries = 100;
const maximumStringLength = 16_384;
const maximumPayloadBytes = 262_144;

export interface SanitizedEventDraft {
  type: string;
  summary: string;
  data: unknown;
}

export function sanitizeEventDraft(
  type: string,
  summary: string,
  data: unknown,
): SanitizedEventDraft {
  const sanitizedSummary = sanitizeString(summary).slice(0, 4096) || 'Runner event';
  const sanitizedData = sanitizeValue(data, 0, new WeakSet<object>());
  let serialized: string;
  try {
    serialized = JSON.stringify(sanitizedData);
  } catch {
    return { type, summary: sanitizedSummary, data: unavailable('unsupported_event_payload') };
  }
  return {
    type,
    summary: sanitizedSummary,
    data:
      Buffer.byteLength(serialized) <= maximumPayloadBytes
        ? sanitizedData
        : unavailable('event_payload_too_large'),
  };
}

function sanitizeValue(value: unknown, depth: number, seen: WeakSet<object>): unknown {
  if (value === null || value === undefined) return null;
  if (typeof value === 'string') return sanitizeString(value).slice(0, maximumStringLength);
  if (typeof value === 'boolean') return value;
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (typeof value === 'bigint') return value.toString();
  if (typeof value === 'symbol' || typeof value === 'function') return '[UNSUPPORTED]';
  if (depth >= maximumDepth) return '[MAX_DEPTH]';
  if (typeof value !== 'object') return null;
  if (seen.has(value)) return '[CIRCULAR]';
  seen.add(value);
  try {
    if (Array.isArray(value))
      return value
        .slice(0, maximumCollectionEntries)
        .map((entry) => sanitizeValue(entry, depth + 1, seen));

    const result: Record<string, unknown> = {};
    for (const [rawKey, entry] of Object.entries(value).slice(0, maximumCollectionEntries)) {
      const key = sanitizeString(rawKey).slice(0, 256) || 'field';
      result[key] = sensitiveKey.test(key) ? '[REDACTED]' : sanitizeValue(entry, depth + 1, seen);
    }
    return result;
  } finally {
    seen.delete(value);
  }
}

function sanitizeString(value: string): string {
  return value
    .replace(ansiSequence, '')
    .replace(controlCharacter, '')
    .replace(privateKey, '[REDACTED PRIVATE KEY]')
    .replace(bearerToken, 'Bearer [REDACTED]')
    .replace(commonToken, '[REDACTED TOKEN]');
}

function unavailable(reason: string): Record<string, unknown> {
  return { redacted: true, reason };
}
