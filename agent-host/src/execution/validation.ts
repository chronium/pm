import { posix } from 'node:path';
import type { RuntimeHandle, RuntimeProcessExecutor } from '../drivers.js';
import type { ValidationStep } from '../protocol/types.js';

const maximumStepOutputBytes = 1024 * 1024;

export interface ValidationStepResult {
  stepId: string;
  displayName: string;
  status: 'passed' | 'failed' | 'skipped';
  exitCode: number | null;
  signal: string | null;
  timedOut: boolean;
  durationMilliseconds: number;
  output: string;
  outputTruncated: boolean;
}

export interface ValidationResult {
  status: 'passed' | 'failed' | 'skipped';
  steps: ValidationStepResult[];
}

export class ValidationRunner {
  constructor(private readonly executor: RuntimeProcessExecutor) {}

  async execute(
    runtime: RuntimeHandle,
    steps: readonly ValidationStep[],
    signal: AbortSignal,
  ): Promise<ValidationResult> {
    const results: ValidationStepResult[] = [];
    let stopped = false;
    for (const step of steps) {
      if (stopped) {
        results.push(skipped(step));
        continue;
      }
      const result = await this.executeStep(runtime, step, signal);
      results.push(result);
      if (result.status === 'failed') stopped = true;
    }
    return {
      status:
        steps.length === 0
          ? 'skipped'
          : results.every((result) => result.status === 'passed')
            ? 'passed'
            : 'failed',
      steps: results,
    };
  }

  static skipped(steps: readonly ValidationStep[]): ValidationResult {
    return { status: 'skipped', steps: steps.map(skipped) };
  }

  private async executeStep(
    runtime: RuntimeHandle,
    step: ValidationStep,
    parentSignal: AbortSignal,
  ): Promise<ValidationStepResult> {
    const started = Date.now();
    const controller = new AbortController();
    let timedOut = false;
    const cancel = (): void => controller.abort(parentSignal.reason);
    parentSignal.addEventListener('abort', cancel, { once: true });
    const timer = setTimeout(() => {
      timedOut = true;
      controller.abort('validation_timeout');
    }, step.timeoutSeconds * 1000);
    let output = '';
    let outputTruncated = false;
    let exitCode: number | null = null;
    let exitSignal: string | null = null;
    try {
      for await (const event of this.executor.execute(
        runtime,
        {
          command: { executable: step.executable, arguments: step.arguments },
          workingDirectory: posix.resolve(
            runtime.agentContext.workspaceDirectory,
            step.workingDirectory,
          ),
          environment: runtime.agentContext.environment,
          standardInput: '',
        },
        controller.signal,
      )) {
        if (event.type === 'exit') {
          exitCode = event.exitCode;
          exitSignal = event.signal;
        } else {
          const prefix = event.type === 'stderr' ? '[stderr] ' : '';
          const chunk = `${prefix}${event.chunk}`;
          const remaining = maximumStepOutputBytes - Buffer.byteLength(output);
          if (remaining > 0) output += Buffer.from(chunk).subarray(0, remaining).toString('utf8');
          if (Buffer.byteLength(chunk) > remaining) outputTruncated = true;
        }
      }
      if (parentSignal.aborted) throw abortError();
    } catch (error) {
      if (parentSignal.aborted) throw error;
      if (!timedOut) throw error;
    } finally {
      clearTimeout(timer);
      parentSignal.removeEventListener('abort', cancel);
    }
    return {
      stepId: step.stepId,
      displayName: step.displayName,
      status: !timedOut && exitCode === 0 && exitSignal === null ? 'passed' : 'failed',
      exitCode,
      signal: exitSignal,
      timedOut,
      durationMilliseconds: Date.now() - started,
      output,
      outputTruncated,
    };
  }
}

function skipped(step: ValidationStep): ValidationStepResult {
  return {
    stepId: step.stepId,
    displayName: step.displayName,
    status: 'skipped',
    exitCode: null,
    signal: null,
    timedOut: false,
    durationMilliseconds: 0,
    output: '',
    outputTruncated: false,
  };
}

function abortError(): Error {
  const error = new Error('Validation was cancelled.');
  error.name = 'AbortError';
  return error;
}
