# Agent runner HTTPS transport 1.0

The runner exposes HTTPS only on an explicitly configured non-wildcard interface. Pairing is the only route that does not require a signed PM identity request, and it requires a short-lived one-use code displayed locally beside the runner certificate fingerprint.

## Pairing

`POST /v1/pairing/complete` accepts a pairing code, the client's supported protocol versions, and the existing PM P-256 identity. The operator must verify the displayed `sha256:<hex>` TLS certificate fingerprint before submitting the code. A successful response selects protocol `1.0`, consumes the code, registers the single client, and returns capabilities. Codes expire after ten minutes and lock after five invalid attempts.

## Authenticated requests

Authenticated requests use these headers:

- `PM-Runner-Client-Id`
- `PM-Runner-Timestamp`, as Unix seconds
- `PM-Runner-Nonce`
- `PM-Runner-Signature`, as base64url P-256 IEEE-P1363 bytes
- `PM-Runner-Protocol-Version`

The signed UTF-8 value is:

```text
pm-runner-auth-v1
<UPPERCASE METHOD>
<RAW PATH AND QUERY>
<PROTOCOL VERSION>
<TIMESTAMP>
<NONCE>
<CLIENT ID>
<LOWERCASE SHA-256 BODY HASH>
```

The runner accepts five minutes of clock skew and durably rejects a reused nonce. It verifies the signature before reporting an incompatible authenticated protocol.

## Discovery and credential lifecycle

- `GET /v1/health` distinguishes authenticated runner reachability from run state.
- `GET /v1/capabilities` returns `AgentRunnerCapabilities`.
- `POST /v1/client/rotate` requires the old request signature and a new-key proof over `pm-runner-rotation-v1`, runner ID, old client ID, new client ID, new public key, and request nonce.
- `DELETE /v1/client` revokes the current client.

Replacing the TLS certificate requires explicit re-pairing in protocol 1.0. Run commands and event transport are defined by the next protocol slice.
