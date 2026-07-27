export const runStates = [
  'requested',
  'accepted',
  'queued',
  'preparing_workspace',
  'starting_runtime',
  'starting_agent',
  'running',
  'validating',
  'collecting_artifacts',
  'completed',
  'failed',
  'cancelled',
] as const;

export type RunState = (typeof runStates)[number];

export interface RunRequest {
  specificationHash: string;
  specification: RunSpecification;
}

export interface RunSpecification {
  protocolVersion: string;
  runId: string;
  requestedAt: string;
  project: {
    projectId: string;
    name: string;
  };
  task: {
    taskId: string;
    title: string;
    revision: string;
  };
  repository: {
    remote: string;
    baseCommit: string;
  };
  agent: {
    providerId: string;
    modelId: string;
    effortId: string;
    promptProfileId: string;
  };
  runtime: {
    runnerId: string;
    profile: RuntimeProfile;
  };
}

export interface RuntimeProfile {
  profileId: string;
  revision: string;
  imageReference: string;
  limits: {
    cpuMillicores: number;
    memoryBytes: number;
    pids: number;
    diskBytes: number;
    timeoutSeconds: number;
  };
  networkProfileId: string;
  validation: ValidationStep[];
  output: {
    mode: 'patch';
    maxPatchBytes: number;
    includeEventLog: boolean;
  };
}

export interface ValidationStep {
  stepId: string;
  displayName: string;
  executable: string;
  arguments: string[];
  workingDirectory: string;
  timeoutSeconds: number;
}

export interface RunEvent {
  protocolVersion: string;
  runId: string;
  sequence: number;
  timestamp: string;
  type: string;
  state: RunState | null;
  summary: string;
  data: unknown;
}

export interface RunArtifact {
  artifactId: string;
  kind: string;
  fileName: string;
  mediaType: string;
  byteLength: number;
  sha256: string;
  createdAt: string;
}
