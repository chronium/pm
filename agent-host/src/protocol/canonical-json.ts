import { createHash, timingSafeEqual } from 'node:crypto';
import type { RunSpecification, RuntimeProfile } from './types.js';

function profileValue(profile: RuntimeProfile, includeRevision: boolean): object {
  return {
    profileId: profile.profileId,
    ...(includeRevision ? { revision: profile.revision } : {}),
    imageReference: profile.imageReference,
    limits: {
      cpuMillicores: profile.limits.cpuMillicores,
      memoryBytes: profile.limits.memoryBytes,
      pids: profile.limits.pids,
      diskBytes: profile.limits.diskBytes,
      timeoutSeconds: profile.limits.timeoutSeconds,
    },
    networkProfileId: profile.networkProfileId,
    validation: profile.validation.map((step) => ({
      stepId: step.stepId,
      displayName: step.displayName,
      executable: step.executable,
      arguments: step.arguments,
      workingDirectory: step.workingDirectory,
      timeoutSeconds: step.timeoutSeconds,
    })),
    output: {
      mode: profile.output.mode,
      maxPatchBytes: profile.output.maxPatchBytes,
      includeEventLog: profile.output.includeEventLog,
    },
  };
}

export function canonicalSpecification(specification: RunSpecification): string {
  return JSON.stringify({
    protocolVersion: specification.protocolVersion,
    runId: specification.runId,
    requestedAt: specification.requestedAt,
    project: {
      projectId: specification.project.projectId,
      name: specification.project.name,
    },
    task: {
      taskId: specification.task.taskId,
      title: specification.task.title,
      revision: specification.task.revision,
    },
    repository: {
      remote: specification.repository.remote,
      baseCommit: specification.repository.baseCommit,
    },
    agent: {
      providerId: specification.agent.providerId,
      modelId: specification.agent.modelId,
      effortId: specification.agent.effortId,
      promptProfileId: specification.agent.promptProfileId,
    },
    runtime: {
      runnerId: specification.runtime.runnerId,
      profile: profileValue(specification.runtime.profile, true),
    },
  });
}

export function canonicalRuntimeProfile(profile: RuntimeProfile): string {
  return JSON.stringify(profileValue(profile, true));
}

export function computeSpecificationHash(specification: RunSpecification): string {
  return sha256(canonicalSpecification(specification));
}

export function computeProfileRevision(profile: RuntimeProfile): string {
  return sha256(JSON.stringify(profileValue(profile, false)));
}

export function fixedTimeHashEquals(left: string, right: string): boolean {
  if (!/^[0-9a-f]{64}$/.test(left) || !/^[0-9a-f]{64}$/.test(right)) return false;
  return timingSafeEqual(Buffer.from(left, 'ascii'), Buffer.from(right, 'ascii'));
}

function sha256(value: string): string {
  return createHash('sha256').update(value, 'utf8').digest('hex');
}
