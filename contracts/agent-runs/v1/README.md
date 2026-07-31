# Agent run protocol v1 fixtures

These fixtures are the language-neutral interoperability examples for agent run protocol 1.x.
The .NET contracts, validators, canonical writers, and expected hashes are normative. Future
runner implementations must deserialize these files and produce the same profile revision and
run specification hash before they can advertise compatibility with the represented protocol
version. Protocol 1.2 adds immutable, explicitly selected wiki-only linked contexts; omitting them
continues to mean a primary-project-only run.

Runtime profile revisions cover the pinned image, resource limits, explicit offline/open network
mode, logical container paths, temporary filesystem size, safe environment allowlist, read-only
caches, and the non-negotiable container security baseline. Host source paths and credential values
never enter the protocol.

The authenticated HTTPS pairing and discovery boundary is defined in `transport.md`.
Protocol readers ignore additive object fields and preserve opaque event data, while rejecting
unknown values that select security, lifecycle, runtime, or authentication behavior. Event type
namespaces are extensible and must be handled generically when they satisfy the protocol grammar.

`run-request.json` contains a complete immutable request. Its specification hash is calculated
from the canonical specification object only; the `specificationHash` property is not part of
the hashed content. Runtime profile revisions similarly exclude their own `revision` property.
