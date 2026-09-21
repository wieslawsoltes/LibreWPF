# Exact ProGPU CI polling resilience

LibreWPF SDK job 103037314459 failed while querying the pinned ProGPU build
(e574a911), with a GitHub API TCP i/o timeout. It had not reached package runtime
qualification. This is distinct from a failed native build or missing artifact.

The staging script now retries failed read-only workflow queries up to three
consecutive attempts, using the existing polling delay and overall deadline.
A successful query resets the consecutive failure count. Exact SHA selection,
terminal non-success rejection, live artifact requirements and every platform
payload check remain unchanged. No old artifact, other commit or local fallback
is admitted. Artifact download failures remain explicit.

Validation: bash syntax check passed. Shell-function fault injection confirmed
that an initial query failure retries and a subsequent completed failed run
still exits nonzero; permanent query failure exits after three attempts without
staging files. Neither test accesses GitHub or writes a runtime directory.
This does not prove final SDK/package qualification. Fresh producer success,
coherent downstream pins and full application/platform checks remain required.

