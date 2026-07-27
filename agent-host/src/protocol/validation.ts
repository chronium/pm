import {
  computeProfileRevision,
  computeSpecificationHash,
  fixedTimeHashEquals,
} from './canonical-json.js';
import {
  runStates,
  type CapabilityManifest,
  type ProviderCapability,
  type RunArtifact,
  type RunRequest,
  type RunState,
  type RuntimeProfile,
} from './types.js';

const runId = /^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/;
const sha256 = /^[0-9a-f]{64}$/;
const gitCommit = /^[0-9a-f]{40}([0-9a-f]{24})?$/;
const timestamp = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/;

export class ProtocolValidationError extends Error {
  constructor(
    public readonly code: string,
    message: string,
  ) {
    super(message);
    this.name = 'ProtocolValidationError';
  }
}

export function parseRunRequest(value: unknown): RunRequest {
  const root = record(value, 'Run request');
  const specificationHash = text(root['specificationHash'], 64, 'Specification hash');
  const specification = record(root['specification'], 'Run specification');
  const project = record(specification['project'], 'Project');
  const task = record(specification['task'], 'Task');
  const repository = record(specification['repository'], 'Repository');
  const agent = record(specification['agent'], 'Agent');
  const runtime = record(specification['runtime'], 'Runtime');
  const profile = record(runtime['profile'], 'Runtime profile');
  const limits = record(profile['limits'], 'Runtime limits');
  const output = record(profile['output'], 'Output policy');
  const validation = array(profile['validation'], 'Validation steps').map((item) => {
    const step = record(item, 'Validation step');
    const argumentsValue = array(step['arguments'], 'Validation arguments').map((argument) =>
      text(argument, 4096, 'Validation argument', true),
    );
    if (argumentsValue.length > 128) invalid('Validation steps support at most 128 arguments.');
    return {
      stepId: id(step['stepId'], 'Validation step ID'),
      displayName: text(step['displayName'], 512, 'Validation display name'),
      executable: text(step['executable'], 1024, 'Validation executable'),
      arguments: argumentsValue,
      workingDirectory: relativePath(step['workingDirectory']),
      timeoutSeconds: positiveInteger(step['timeoutSeconds'], 'Validation timeout'),
    };
  });

  if (
    validation.length > 64 ||
    new Set(validation.map((step) => step.stepId)).size !== validation.length
  )
    invalid('Validation steps must be unique and contain at most 64 entries.');

  const outputMode = text(output['mode'], 32, 'Output mode');
  if (outputMode !== 'patch') invalid('Protocol 1.0 supports patch output only.');

  const request: RunRequest = {
    specificationHash,
    specification: {
      protocolVersion: text(specification['protocolVersion'], 16, 'Protocol version'),
      runId: text(specification['runId'], 128, 'Run ID'),
      requestedAt: canonicalTimestamp(specification['requestedAt'], 'Requested timestamp'),
      project: {
        projectId: text(project['projectId'], 256, 'Project ID'),
        name: text(project['name'], 512, 'Project name'),
      },
      task: {
        taskId: text(task['taskId'], 256, 'Task ID'),
        title: text(task['title'], 1024, 'Task title'),
        revision: text(task['revision'], 64, 'Task revision'),
      },
      repository: {
        remote: text(repository['remote'], 2048, 'Repository remote'),
        baseCommit: text(repository['baseCommit'], 64, 'Base commit'),
      },
      agent: {
        providerId: id(agent['providerId'], 'Agent provider'),
        modelId: id(agent['modelId'], 'Agent model'),
        effortId: id(agent['effortId'], 'Agent effort'),
        promptProfileId: id(agent['promptProfileId'], 'Prompt profile'),
      },
      runtime: {
        runnerId: id(runtime['runnerId'], 'Runner ID'),
        profile: {
          profileId: id(profile['profileId'], 'Runtime profile ID'),
          revision: text(profile['revision'], 64, 'Runtime profile revision'),
          imageReference: text(profile['imageReference'], 2048, 'Image reference'),
          limits: {
            cpuMillicores: positiveInteger(limits['cpuMillicores'], 'CPU limit'),
            memoryBytes: positiveInteger(limits['memoryBytes'], 'Memory limit'),
            pids: positiveInteger(limits['pids'], 'PID limit'),
            diskBytes: positiveInteger(limits['diskBytes'], 'Disk limit'),
            timeoutSeconds: positiveInteger(limits['timeoutSeconds'], 'Runtime timeout'),
          },
          networkProfileId: id(profile['networkProfileId'], 'Network profile'),
          validation,
          output: {
            mode: 'patch',
            maxPatchBytes: positiveInteger(output['maxPatchBytes'], 'Maximum patch size'),
            includeEventLog: boolean(output['includeEventLog'], 'Include event log'),
          },
        },
      },
    },
  };

  validateCanonicalHashes(request);
  return request;
}

export function parseCapabilityManifest(value: unknown): CapabilityManifest {
  const root = record(value, 'Capability manifest');
  const providers = array(root['agentProviders'], 'Agent providers').map(parseProviderCapability);
  const runtimeProfiles = array(root['runtimeProfiles'], 'Runtime profiles').map((profile) =>
    parseRuntimeProfile(profile),
  );
  if (
    providers.length === 0 ||
    providers.length > 32 ||
    new Set(providers.map((provider) => provider.providerId)).size !== providers.length
  )
    invalid('Agent providers must be unique and contain between 1 and 32 entries.');
  if (
    runtimeProfiles.length === 0 ||
    runtimeProfiles.length > 64 ||
    new Set(runtimeProfiles.map((profile) => profile.profileId)).size !== runtimeProfiles.length
  )
    invalid('Runtime profiles must be unique and contain between 1 and 64 entries.');

  return {
    displayName: text(root['displayName'], 512, 'Runner display name'),
    agentProviders: providers,
    runtimeProfiles,
  };
}

export function parseRuntimeProfile(value: unknown): RuntimeProfile {
  const profile = record(value, 'Runtime profile');
  const limits = record(profile['limits'], 'Runtime limits');
  const output = record(profile['output'], 'Output policy');
  const validation = array(profile['validation'], 'Validation steps').map((item) => {
    const step = record(item, 'Validation step');
    const argumentsValue = array(step['arguments'], 'Validation arguments').map((argument) =>
      text(argument, 4096, 'Validation argument', true),
    );
    if (argumentsValue.length > 128) invalid('Validation steps support at most 128 arguments.');
    return {
      stepId: id(step['stepId'], 'Validation step ID'),
      displayName: text(step['displayName'], 512, 'Validation display name'),
      executable: text(step['executable'], 1024, 'Validation executable'),
      arguments: argumentsValue,
      workingDirectory: relativePath(step['workingDirectory']),
      timeoutSeconds: positiveInteger(step['timeoutSeconds'], 'Validation timeout'),
    };
  });
  if (
    validation.length > 64 ||
    new Set(validation.map((step) => step.stepId)).size !== validation.length
  )
    invalid('Validation steps must be unique and contain at most 64 entries.');
  const outputMode = text(output['mode'], 32, 'Output mode');
  if (outputMode !== 'patch') invalid('Protocol 1.0 supports patch output only.');
  const result: RuntimeProfile = {
    profileId: id(profile['profileId'], 'Runtime profile ID'),
    revision: text(profile['revision'], 64, 'Runtime profile revision'),
    imageReference: text(profile['imageReference'], 2048, 'Image reference'),
    limits: {
      cpuMillicores: positiveInteger(limits['cpuMillicores'], 'CPU limit'),
      memoryBytes: positiveInteger(limits['memoryBytes'], 'Memory limit'),
      pids: positiveInteger(limits['pids'], 'PID limit'),
      diskBytes: positiveInteger(limits['diskBytes'], 'Disk limit'),
      timeoutSeconds: positiveInteger(limits['timeoutSeconds'], 'Runtime timeout'),
    },
    networkProfileId: id(profile['networkProfileId'], 'Network profile'),
    validation,
    output: {
      mode: 'patch',
      maxPatchBytes: positiveInteger(output['maxPatchBytes'], 'Maximum patch size'),
      includeEventLog: boolean(output['includeEventLog'], 'Include event log'),
    },
  };
  if (!sha256.test(result.revision)) invalid('Runtime profile revision is invalid.');
  const expected = computeProfileRevision(result);
  if (!fixedTimeHashEquals(expected, result.revision))
    throw new ProtocolValidationError(
      'profile_revision_mismatch',
      'Runtime profile revision does not match its canonical snapshot.',
    );
  return result;
}

function validateCanonicalHashes(request: RunRequest): void {
  const specification = request.specification;
  if (specification.protocolVersion !== '1.0')
    throw new ProtocolValidationError('incompatible_protocol', 'Only protocol 1.0 is supported.');
  if (!runId.test(specification.runId)) invalid('Run ID is not URL-safe.');
  if (!sha256.test(specification.task.revision)) invalid('Task revision must be a SHA-256 hash.');
  if (!gitCommit.test(specification.repository.baseCommit)) invalid('Base commit is invalid.');
  if (!sha256.test(request.specificationHash)) invalid('Specification hash is invalid.');
  if (!sha256.test(specification.runtime.profile.revision)) invalid('Profile revision is invalid.');

  const expectedProfile = computeProfileRevision(specification.runtime.profile);
  if (!fixedTimeHashEquals(expectedProfile, specification.runtime.profile.revision))
    throw new ProtocolValidationError(
      'profile_revision_mismatch',
      'Runtime profile revision does not match its canonical snapshot.',
    );

  const expectedSpecification = computeSpecificationHash(specification);
  if (!fixedTimeHashEquals(expectedSpecification, request.specificationHash))
    throw new ProtocolValidationError(
      'specification_hash_mismatch',
      'Specification hash does not match its canonical snapshot.',
    );
}

function parseProviderCapability(value: unknown): ProviderCapability {
  const provider = record(value, 'Agent provider');
  const modelIds = identifiers(provider['modelIds'], 'Model IDs');
  const effortIds = identifiers(provider['effortIds'], 'Effort IDs');
  const defaultModelId = optionalIdentifier(provider['defaultModelId'], 'Default model ID');
  const defaultEffortId = optionalIdentifier(provider['defaultEffortId'], 'Default effort ID');
  if (modelIds.length === 0 || effortIds.length === 0)
    invalid('Agent providers require at least one model and effort.');
  if (defaultModelId !== null && !modelIds.includes(defaultModelId))
    invalid('Default model must be advertised by its provider.');
  if (defaultEffortId !== null && !effortIds.includes(defaultEffortId))
    invalid('Default effort must be advertised by its provider.');
  return {
    providerId: id(provider['providerId'], 'Agent provider'),
    modelIds,
    defaultModelId,
    effortIds,
    defaultEffortId,
  };
}

export function isRunState(value: string): value is RunState {
  return (runStates as readonly string[]).includes(value);
}

export function validateArtifact(artifact: RunArtifact): void {
  if (
    !isBoundedText(artifact.artifactId, 256) ||
    !isBoundedText(artifact.kind, 256) ||
    artifact.fileName.length === 0 ||
    artifact.fileName.length > 512 ||
    artifact.fileName.includes('/') ||
    artifact.fileName.includes('\\') ||
    artifact.mediaType.length === 0 ||
    artifact.mediaType.length > 256 ||
    !Number.isSafeInteger(artifact.byteLength) ||
    artifact.byteLength < 0 ||
    !sha256.test(artifact.sha256) ||
    !timestamp.test(artifact.createdAt)
  )
    invalid('Artifact metadata is invalid.');
}

function record(value: unknown, name: string): Record<string, unknown> {
  if (value === null || typeof value !== 'object' || Array.isArray(value))
    invalid(`${name} is required.`);
  return value as Record<string, unknown>;
}

function array(value: unknown, name: string): unknown[] {
  if (!Array.isArray(value)) invalid(`${name} must be an array.`);
  return value;
}

function text(value: unknown, maximum: number, name: string, allowEmpty = false): string {
  if (
    typeof value !== 'string' ||
    (!allowEmpty && value.length === 0) ||
    value.length > maximum ||
    value !== value.trim() ||
    [...value].some((character) => /[\u0000-\u001f\u007f]/.test(character))
  )
    invalid(`${name} is invalid.`);
  return value;
}

function id(value: unknown, name: string): string {
  return text(value, 256, name);
}

function optionalIdentifier(value: unknown, name: string): string | null {
  return value === null || value === undefined ? null : id(value, name);
}

function identifiers(value: unknown, name: string): string[] {
  const result = array(value, name).map((item) => id(item, name));
  if (result.length > 128 || new Set(result).size !== result.length)
    invalid(`${name} must be unique and contain at most 128 entries.`);
  return result;
}

function positiveInteger(value: unknown, name: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value <= 0)
    invalid(`${name} must be a positive safe integer.`);
  return value;
}

function boolean(value: unknown, name: string): boolean {
  if (typeof value !== 'boolean') invalid(`${name} must be a boolean.`);
  return value;
}

function canonicalTimestamp(value: unknown, name: string): string {
  const result = text(value, 32, name);
  if (!timestamp.test(result) || Number.isNaN(Date.parse(result))) invalid(`${name} is invalid.`);
  return result;
}

function relativePath(value: unknown): string {
  const result = text(value, 1024, 'Working directory');
  if (result.startsWith('/') || /^[A-Za-z]:[\\/]/.test(result))
    invalid('Working directory must be relative.');
  if (result.split(/[\\/]/).some((segment) => segment === '..'))
    invalid('Working directory cannot escape the workspace.');
  return result;
}

function invalid(message: string): never {
  throw new ProtocolValidationError('invalid_run_specification', message);
}

function isBoundedText(value: string, maximum: number): boolean {
  return (
    value.length > 0 &&
    value.length <= maximum &&
    value === value.trim() &&
    ![...value].some((character) => /[\u0000-\u001f\u007f]/.test(character))
  );
}
