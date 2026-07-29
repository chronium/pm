export interface PairingInstructions {
  runnerId: string;
  code: string;
  tlsFingerprint: string;
  expiresIn: string;
}

export function formatPairingInstructions(instructions: PairingInstructions): string {
  return [
    `Runner: ${instructions.runnerId}`,
    `Pairing code: ${instructions.code}`,
    `TLS fingerprint: ${instructions.tlsFingerprint}`,
    `Expires in: ${instructions.expiresIn}`,
    '',
  ].join('\n');
}
