import assert from 'node:assert/strict';
import { chmodSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';
import { CredentialStore } from '../src/auth/credential-store.js';
import type { HostConfig } from '../src/config.js';
import { formatDoctorReport, runDoctor } from '../src/doctor.js';
import { RunStore } from '../src/persistence/run-store.js';
import {
  createArtifactManifest,
  createReleaseInfo,
  materializeCapabilities,
} from '../src/release-tool.js';
import { parseReleaseInfo } from '../src/release-info.js';
import {
  createCapabilityManifest,
  createRuntimeProbe,
  createTempDirectory,
  createTestCertificate,
} from './helpers.js';

test('release metadata binds immutable image capabilities and checksummed artifacts', () => {
  const temporary = createTempDirectory();
  try {
    const imageDigest = `sha256:${'a'.repeat(64)}`;
    const imageReference = `localhost/pm-agent-worker@${imageDigest}`;
    const release = createReleaseInfo(
      '0.1.0',
      'b'.repeat(40),
      '2026-07-29T00:00:00.000Z',
      imageReference,
      imageDigest,
    );
    const template = createCapabilityManifest();
    const capabilities = materializeCapabilities(template, imageReference);
    assert.equal(capabilities.runtimeProfiles[0]?.imageReference, imageReference);
    assert.match(capabilities.runtimeProfiles[0]?.revision ?? '', /^[0-9a-f]{64}$/);

    const first = join(temporary.path, 'host.tar.gz');
    const second = join(temporary.path, 'worker.oci.tar');
    writeFileSync(first, 'host');
    writeFileSync(second, 'worker');
    const manifest = createArtifactManifest(release, [second, first]);
    assert.deepEqual(
      manifest.artifacts.map((artifact) => artifact.fileName),
      ['host.tar.gz', 'worker.oci.tar'],
    );
    assert.equal(manifest.workerImageDigest, imageDigest);
    assert.deepEqual(parseReleaseInfo(release), release);
  } finally {
    temporary.dispose();
  }
});

test('doctor verifies configuration, storage, databases, and installed runtime', () => {
  const temporary = createTempDirectory();
  const tls = createTestCertificate(temporary.path);
  const capabilityPath = join(temporary.path, 'capabilities.json');
  const repositoryPath = join(temporary.path, 'repositories.json');
  const authenticationPath = join(temporary.path, 'codex-auth.json');
  const releasePath = join(temporary.path, 'release-info.json');
  writePrivate(capabilityPath, JSON.stringify(createCapabilityManifest()));
  writePrivate(
    repositoryPath,
    JSON.stringify({
      repositories: [{ remote: 'https://github.com/chronium/pm-agent-smoke.git' }],
    }),
  );
  writePrivate(authenticationPath, '{}');
  writePrivate(
    releasePath,
    JSON.stringify(
      createReleaseInfo(
        '0.1.0',
        'c'.repeat(40),
        '2026-07-29T00:00:00.000Z',
        `localhost/pm-agent-worker@sha256:${'d'.repeat(64)}`,
        `sha256:${'d'.repeat(64)}`,
      ),
    ),
  );
  new RunStore(temporary.path).close();
  new CredentialStore(temporary.path).close();
  const config: HostConfig = {
    dataRoot: temporary.path,
    maxConcurrency: 1,
    queueCapacity: 32,
    retentionDays: 30,
    minimumFreeDiskBytes: 1,
    listenAddress: '127.0.0.1',
    port: 7443,
    tlsCertificatePath: tls.certificatePath,
    tlsKeyPath: tls.keyPath,
    capabilityManifestPath: capabilityPath,
    repositoryPolicyPath: repositoryPath,
    codexAuthPath: authenticationPath,
    releaseManifestPath: releasePath,
  };
  try {
    const report = runDoctor(config, {
      platform: () => 'linux',
      architecture: () => 'x64',
      nodeVersion: () => 'v26.5.0',
      userId: () => 1000,
      runtimeProbe: createRuntimeProbe(),
      now: () => new Date('2026-07-29T00:00:00.000Z'),
    });
    assert.equal(report.ok, true);
    assert.equal(
      report.checks.every((check) => check.status === 'pass'),
      true,
    );
    assert.match(formatDoctorReport(report), /Runner is ready/);

    chmodSync(authenticationPath, 0o644);
    const unsafe = runDoctor(config, {
      platform: () => 'linux',
      architecture: () => 'x64',
      nodeVersion: () => 'v26.5.0',
      userId: () => 1000,
      runtimeProbe: createRuntimeProbe(),
    });
    assert.equal(unsafe.ok, false);
    assert.equal(
      unsafe.checks.find((check) => check.id === 'codex_authentication')?.status,
      'failure',
    );
  } finally {
    temporary.dispose();
  }
});

test('packaged systemd unit runs doctor before the versioned host binary', () => {
  const unit = readFileSync(join(process.cwd(), 'systemd', 'pm-agent-host.service'), 'utf8');
  assert.match(unit, /ExecStartPre=.*pm-agent-host doctor/);
  assert.match(unit, /ExecStart=.*pm-agent-host serve/);
  assert.doesNotMatch(unit, /WorkingDirectory=/);
  assert.doesNotMatch(unit, /^NoNewPrivileges=true$/m);
  assert.doesNotMatch(unit, /^(PrivateTmp|LockPersonality|RestrictAddressFamilies)=/m);
  assert.match(unit, /Rootless Podman needs newuidmap and its own namespaces/);
  assert.match(unit, /StandardError=journal/);
});

test('packaged host wrapper loads the owner configuration for operator commands', () => {
  const release = readFileSync(join(process.cwd(), 'release', 'build-linux-release.sh'), 'utf8');
  assert.match(release, /environment_file=.*pm-agent-host\/host\.env/);
  assert.match(release, /\. \"\$environment_file\"/);
});

test('release artifacts are verified before atomically replacing the published directory', () => {
  const release = readFileSync(join(process.cwd(), 'release', 'build-linux-release.sh'), 'utf8');
  const verification = release.indexOf('sha256sum --check SHA256SUMS');
  const publication = release.indexOf('mv "$staged_artifacts" "$artifact_root"');
  assert.notEqual(verification, -1);
  assert.notEqual(publication, -1);
  assert.ok(verification < publication);
  assert.match(release, /\.\$\{artifact_name\}\.staging\.XXXXXX/);
});

test('installer configuration accepts authentication already stored at its destination', () => {
  const installer = readFileSync(join(process.cwd(), 'release', 'install.sh'), 'utf8');
  assert.match(installer, /authentication_destination=/);
  assert.match(installer, /readlink -f "\$authentication_source"/);
});

test('installer verifies the image and atomically replaces its read-only installed copy', () => {
  const installer = readFileSync(join(process.cwd(), 'release', 'install.sh'), 'utf8');
  const imageVerification = installer.indexOf('podman image exists "$image_reference"');
  const activation = installer.indexOf('ln -sfn "$destination" "$install_root/current"');
  assert.notEqual(imageVerification, -1);
  assert.notEqual(activation, -1);
  assert.ok(imageVerification < activation);
  assert.match(installer, /installer_next=.*install\.sh\.next/);
  assert.match(installer, /mv "\$installer_next" "\$install_root\/install\.sh"/);
});

function writePrivate(path: string, value: string): void {
  writeFileSync(path, value, { mode: 0o600 });
  chmodSync(path, 0o600);
}
