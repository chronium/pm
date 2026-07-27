# Agent run protocol v1 fixtures

These fixtures are the language-neutral interoperability examples for agent run protocol 1.0.
The .NET contracts, validators, canonical writers, and expected hashes are normative. Future
runner implementations must deserialize these files and produce the same profile revision and
run specification hash before they can advertise protocol 1.0 compatibility.

`run-request.json` contains a complete immutable request. Its specification hash is calculated
from the canonical specification object only; the `specificationHash` property is not part of
the hashed content. Runtime profile revisions similarly exclude their own `revision` property.
