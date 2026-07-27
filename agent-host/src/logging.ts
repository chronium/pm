export type LogLevel = 'info' | 'warn' | 'error';

export type LogFields = Partial<{
  runId: string;
  state: string;
  queueDepth: number;
  activeRuns: number;
  durationMs: number;
  errorCode: string;
  recoveredRuns: number;
  prunedRuns: number;
}>;

const fieldNames = new Set([
  'runId',
  'state',
  'queueDepth',
  'activeRuns',
  'durationMs',
  'errorCode',
  'recoveredRuns',
  'prunedRuns',
]);

export class JsonLogger {
  constructor(
    private readonly write: (line: string) => void = (line) => process.stderr.write(`${line}\n`),
    private readonly now: () => Date = () => new Date(),
  ) {}

  info(event: string, fields: LogFields = {}): void {
    this.log('info', event, fields);
  }

  warn(event: string, fields: LogFields = {}): void {
    this.log('warn', event, fields);
  }

  error(event: string, fields: LogFields = {}): void {
    this.log('error', event, fields);
  }

  private log(level: LogLevel, event: string, fields: LogFields): void {
    const record: Record<string, string | number> = {
      timestamp: this.now().toISOString(),
      level,
      event: sanitize(event),
    };
    for (const [name, value] of Object.entries(fields)) {
      if (!fieldNames.has(name) || value === undefined) continue;
      record[name] = typeof value === 'string' ? sanitize(value) : value;
    }
    this.write(JSON.stringify(record));
  }
}

function sanitize(value: string): string {
  return value.replace(/[\u0000-\u001f\u007f]/g, '').slice(0, 256);
}
