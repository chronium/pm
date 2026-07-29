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

export const runFailureStages = [
  'workspace',
  'runtime',
  'agent',
  'validation',
  'artifacts',
  'cancellation',
  'system',
] as const;

export type RunFailureStage = (typeof runFailureStages)[number];

export const runFailureCodes = [
  'repository_not_allowed',
  'repository_fetch_failed',
  'base_revision_unavailable',
  'task_revision_mismatch',
  'workspace_policy_unsupported',
  'workspace_preparation_failed',
  'runtime_start_failed',
  'runtime_resource_limit',
  'runtime_timeout',
  'runtime_cleanup_failed',
  'agent_start_failed',
  'agent_execution_failed',
  'validation_failed',
  'validation_timeout',
  'artifact_collection_failed',
  'artifact_collection_timeout',
  'run_cancelled',
  'runner_restarted',
  'internal_failure',
] as const;

export type RunFailureCode = (typeof runFailureCodes)[number];

export interface RunFailure {
  code: RunFailureCode;
  stage: RunFailureStage;
  summary: string;
  recommendedAction: string;
  retryable: boolean;
}

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
  network: RuntimeNetworkPolicy;
  container: RuntimeContainerPolicy;
  validation: ValidationStep[];
  output: {
    mode: 'patch';
    maxPatchBytes: number;
    includeEventLog: boolean;
  };
}

export interface RuntimeNetworkPolicy {
  profileId: string;
  mode: 'offline' | 'open';
}

export interface RuntimeContainerPolicy {
  workspacePath: string;
  codexHomePath: string;
  temporaryPath: string;
  temporaryBytes: number;
  environmentAllowlist: string[];
  readOnlyCaches: RuntimeCacheMount[];
  security: {
    readOnlyRootFilesystem: true;
    userNamespace: 'keep-id';
    noNewPrivileges: true;
    dropAllCapabilities: true;
    privateNamespaces: true;
    seccompProfile: 'runtime-default';
    lsmProfile: 'none';
  };
}

export interface RuntimeCacheMount {
  cacheId: string;
  containerPath: string;
}

export interface ValidationStep {
  stepId: string;
  displayName: string;
  executable: string;
  arguments: string[];
  workingDirectory: string;
  timeoutSeconds: number;
}

export interface ProviderCapability {
  providerId: string;
  modelIds: string[];
  defaultModelId: string | null;
  effortIds: string[];
  defaultEffortId: string | null;
}

export interface CapabilityManifest {
  displayName: string;
  agentProviders: ProviderCapability[];
  runtimeProfiles: RuntimeProfile[];
}

export interface RunnerCapabilities extends CapabilityManifest {
  runnerId: string;
  protocolVersions: string[];
  operatingSystem: string;
  architecture: string;
  containerRuntime: ContainerRuntimeCapability;
  capacity: {
    maximumRuns: number;
    activeRuns: number;
    memoryBytes: number;
  };
}

export interface ContainerRuntimeCapability {
  engineId: 'podman';
  version: string;
  rootless: boolean;
  cgroupVersion: string;
  cgroupManager: string;
  seccompEnabled: boolean;
  selinuxEnabled: boolean;
  appArmorEnabled: boolean;
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
