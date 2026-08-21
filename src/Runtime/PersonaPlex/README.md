# PersonaPlex runtime credential contract

The adapter accepts private streaming traffic only at `/stream` with an
`Authorization: Bearer <PERSONAPLEX_ADAPTER_TOKEN>` header. Missing credentials
receive `401`; invalid credentials receive `403`; the credential value is never
included in responses or logs.

Task 2 must have AppHost generate one `PERSONAPLEX_ADAPTER_TOKEN` secret and
inject the same value into the Kernel and runtime resources. Kernel uses it only
to call the runtime's internal container-network endpoint. The Hugging Face
`HF_TOKEN` is injected into the runtime only; it must not be projected to
Kernel, Flutter, logs, health responses, or the adapter protocol.

The image does not publish a host port. Aspire must place Kernel and runtime on
the same private resource network and configure the internal endpoint.

## Dependency reproducibility

`uv.lock` is the fully resolved, hash-validated Python dependency graph. The
Docker build installs its exported requirements with `pip --require-hashes`.
The CUDA base image is pinned by digest; its OS package contents are therefore
fixed by that immutable image boundary rather than resolved from a mutable apt
repository during the adapter dependency install.

PyTorch `cu130` Blackwell wheels also need host CUDA shared libraries that the
base image does not put on the default loader path. The Dockerfile therefore
installs `libcusparselt0-cuda-13` and `libnvshmem3-cuda-13`, then registers
`/usr/lib/x86_64-linux-gnu/libcusparseLt/13` and
`/usr/lib/x86_64-linux-gnu/nvshmem/13` with `ldconfig`. Without those entries,
`import torch` fails with `libnvshmem_host.so.3: cannot open shared object file`.

Torch is installed with `--no-deps`, so `pytorch-blackwell.lock` also pins
Triton 3.5.1. Moshi warmup uses `torch.compile` / inductor and fails with
`TritonMissing` when that wheel is absent. On GPUs with under ~22 GB VRAM the
adapter prefers `--cpu-offload` first because the BF16 weights alone are ~16.7 GB.
