import type {
  AgentRunnerRegistration,
  AgentRunnerStatus,
  AgentRunPreflightResult,
  AgentRunRemoteStart,
} from './agent-runs-api.service';

export const runnerRegistration: AgentRunnerRegistration = {
  runnerId: 'runner-linux',
  displayName: 'Linux workstation',
  endpoint: 'https://100.85.255.100:7443',
  tlsFingerprint: 'sha256:' + 'ab'.repeat(32),
  protocolVersion: '1.0',
  clientId: 'pm-angular',
  clientFingerprint: 'sha256:' + 'cd'.repeat(32),
  pairedAt: '2026-07-29T08:00:00.000Z',
};

export const runnerStatus: AgentRunnerStatus = {
  registration: runnerRegistration,
  health: {
    runnerId: runnerRegistration.runnerId,
    status: 'online',
    protocolVersion: '1.0',
    timestamp: '2026-07-29T08:01:00.000Z',
  },
  capabilities: {
    runnerId: runnerRegistration.runnerId,
    displayName: runnerRegistration.displayName,
    protocolVersions: ['1.0'],
    operatingSystem: 'linux',
    architecture: 'x64',
    containerRuntime: {
      engineId: 'podman',
      version: '5.5.2',
      rootless: true,
      cgroupVersion: 'v2',
      cgroupManager: 'systemd',
      seccompEnabled: true,
      selinuxEnabled: false,
      appArmorEnabled: false,
    },
    capacity: { maximumRuns: 3, activeRuns: 1, memoryBytes: 64 * 1024 * 1024 * 1024 },
    agentProviders: [
      {
        providerId: 'codex',
        modelIds: ['gpt-5.4', 'gpt-5.4-mini'],
        defaultModelId: 'gpt-5.4',
        effortIds: ['low', 'medium', 'high'],
        defaultEffortId: 'medium',
      },
    ],
    runtimeProfiles: [
      {
        profileId: 'pm-development',
        revision: 'profile-r1',
        imageReference: 'localhost/pm-agent-development@sha256:' + 'ef'.repeat(32),
        limits: {
          cpuMillicores: 6000,
          memoryBytes: 12 * 1024 * 1024 * 1024,
          pids: 1024,
          diskBytes: 20 * 1024 * 1024 * 1024,
          timeoutSeconds: 10800,
        },
        network: { profileId: 'development-open', mode: 'open' },
        container: {
          workspacePath: '/workspace',
          codexHomePath: '/codex-home',
          temporaryPath: '/tmp',
          temporaryBytes: 1024 * 1024 * 1024,
          environmentAllowlist: ['PATH', 'HOME', 'CODEX_HOME'],
          readOnlyCaches: [],
          security: {
            readOnlyRootFilesystem: true,
            userNamespace: 'private',
            noNewPrivileges: true,
            dropAllCapabilities: true,
            privateNamespaces: true,
            seccompProfile: 'default',
            lsmProfile: 'none',
          },
        },
        validation: [
          {
            stepId: 'frontend',
            displayName: 'Frontend validation',
            executable: 'npm',
            arguments: ['run', 'frontend:validate'],
            workingDirectory: '.',
            timeoutSeconds: 1800,
          },
        ],
        output: { mode: 'patch', maxPatchBytes: 10 * 1024 * 1024, includeEventLog: true },
      },
    ],
  },
  revision: 'runner-r1',
};

export const readyPreflight: AgentRunPreflightResult = {
  ready: true,
  runId: 'run-01K123',
  revision: 'draft-r1',
  checks: [
    { id: 'git-clean', label: 'Git workspace', status: 'passed', summary: 'Workspace is clean.' },
    {
      id: 'runner-capacity',
      label: 'Runner capacity',
      status: 'passed',
      summary: 'Two slots are available.',
    },
  ],
  request: {
    specificationHash: 'hash-r1',
    specification: {
      protocolVersion: '1.0',
      runId: 'run-01K123',
      requestedAt: '2026-07-29T08:02:00.000Z',
      project: { projectId: 'pm-project', name: 'PM' },
      task: { taskId: 'AGENT-0010', title: 'Angular runner launch', revision: 'task-r1' },
      repository: {
        remote: 'https://github.com/chronium/pm.git',
        baseCommit: '1234567890abcdef1234567890abcdef12345678',
      },
      agent: {
        providerId: 'codex',
        modelId: 'gpt-5.4',
        effortId: 'medium',
        promptProfileId: 'task-execution',
      },
      runtime: {
        runnerId: runnerRegistration.runnerId,
        profile: runnerStatus.capabilities.runtimeProfiles[0]!,
      },
    },
  },
};

export const acceptedRun: AgentRunRemoteStart = {
  disposition: 'new',
  run: {
    runId: 'run-01K123',
    specificationHash: 'hash-r1',
    specification: readyPreflight.request!.specification,
    state: 'accepted',
    lastEventSequence: 1,
    acceptedAt: '2026-07-29T08:03:00.000Z',
    updatedAt: '2026-07-29T08:03:00.000Z',
    terminalAt: null,
    cancellationRequestedAt: null,
    agentThreadId: null,
  },
};
