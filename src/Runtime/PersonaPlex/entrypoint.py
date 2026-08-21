from __future__ import annotations

"""Private PCM adapter for the pinned NVIDIA PersonaPlex server.

This process deliberately exposes no token, prompt, text, or model-path data.
Only the loopback NVIDIA server receives the Hugging Face credential.
"""

import asyncio
from dataclasses import dataclass
import hmac
import logging
import os
import subprocess
from typing import Final

try:
    from aiohttp import ClientSession, WSMsgType, web
    import numpy as np
    import sphn
except ImportError:  # Enables pure-state tests without installing the GPU image dependencies.
    ClientSession = WSMsgType = web = np = sphn = None


UPSTREAM_HOST: Final = "127.0.0.1"
UPSTREAM_PORT: Final = 8998
PCM_SAMPLE_RATE: Final = 24000
PCM_FRAME_SAMPLES: Final = 1920
PCM_FRAME_BYTES: Final = PCM_FRAME_SAMPLES * 2
MAX_BUFFERED_FRAMES: Final = 4
# The first model load includes a multi-gigabyte Hugging Face download and CUDA
# warm-up. Do not mistake that normal cold-start work for a failed runtime.
UPSTREAM_STARTUP_TIMEOUT_SECONDS: Final = 600
# System-prompt priming on a 16GB GPU can take well over a few seconds. A short
# receive timeout makes the adapter reconnect while moshi still holds its lock.
UPSTREAM_HANDSHAKE_TIMEOUT_SECONDS: Final = 180

LOGGER = logging.getLogger("personaplex-adapter")


def _require_dependencies() -> None:
    if None in (ClientSession, WSMsgType, web, np, sphn):
        raise RuntimeError("PersonaPlex adapter dependencies are not installed.")


@dataclass
class RuntimeState:
    _state: str = "downloading"
    _mode: str = "unavailable"
    _message: str = "PersonaPlex runtime is starting."

    def set_loading(self, message: str) -> None:
        self._set("loading", "unavailable", message)

    def set_ready(self, message: str, *, mode: str) -> None:
        self._set("ready", mode, message)

    def set_failed(self, message: str) -> None:
        self._set("failed", "unavailable", message)

    def _set(self, state: str, mode: str, message: str) -> None:
        self._state, self._mode, self._message = state, mode, message
        LOGGER.info("readiness_transition state=%s mode=%s", state, mode)

    def health(self, _token: str | None = None, _cache_path: str | None = None) -> dict[str, str]:
        """Return the public readiness shape; arguments exist for leak-regression tests."""
        return {"state": self._state, "mode": self._mode, "message": self._message}


def server_command(*, cpu_offload: bool) -> list[str]:
    command = [
        "python3",
        "-m",
        "moshi.server",
        "--host",
        UPSTREAM_HOST,
        "--port",
        str(UPSTREAM_PORT),
        "--static",
        "none",
        "--device",
        "cuda",
    ]
    if cpu_offload:
        command.append("--cpu-offload")
    return command


def server_environment() -> dict[str, str]:
    # Moshi warmup uses torch.compile/inductor. On this CUDA 13 + 16GB stack that
    # path is fragile (Triton JIT + cuDNN), so run the official server without it.
    environment = os.environ.copy()
    environment["TORCHDYNAMO_DISABLE"] = "1"
    environment["TORCH_COMPILE_DISABLE"] = "1"
    environment["TORCH_CUDNN_V8_API_DISABLED"] = "1"
    environment["CUDNN_CONV_ALGOS_SEARCH"] = "HEURISTIC"
    environment["PYTORCH_CUDA_ALLOC_CONF"] = "expandable_segments:True"
    return environment


async def health_handler(request: web.Request) -> web.Response:
    state: RuntimeState = request.app["runtime_state"]
    return web.json_response(state.health())


async def ready_handler(request: web.Request) -> web.Response:
    state: RuntimeState = request.app["runtime_state"]
    status = 200 if state.health()["state"] == "ready" else 503
    return web.json_response(state.health(), status=status)


def _pcm16_to_float(payload: bytes) -> np.ndarray:
    validate_pcm_frame(payload)
    return np.frombuffer(payload, dtype="<i2").astype(np.float32) / 32768.0


def validate_pcm_frame(payload: bytes) -> None:
    if len(payload) != PCM_FRAME_BYTES:
        raise ValueError("PCM frames must contain exactly 1,920 samples.")


def _float_to_pcm16(pcm: np.ndarray) -> bytes:
    normalized = np.clip(pcm, -1.0, 1.0)
    return (normalized * 32767.0).astype("<i2").tobytes()


async def stream_handler(request: web.Request) -> web.StreamResponse:
    adapter_token: str | None = request.app.get("adapter_token")
    authorization = request.headers.get("Authorization")
    if not adapter_token:
        LOGGER.error("stream_authorization_failed reason=adapter_configuration")
        return web.json_response({"error": "unavailable"}, status=503)
    if not authorization:
        LOGGER.warning("stream_authorization_failed reason=missing")
        return web.json_response({"error": "unauthorized"}, status=401)
    presented = authorization.removeprefix("Bearer ")
    if presented == authorization or not hmac.compare_digest(presented, adapter_token):
        LOGGER.warning("stream_authorization_failed reason=invalid")
        return web.json_response({"error": "unauthorized"}, status=403)

    _require_dependencies()
    state: RuntimeState = request.app["runtime_state"]
    if state.health()["state"] != "ready":
        return web.json_response(state.health(), status=503)

    # Accept the Kernel socket immediately. Delaying the 101 until moshi finishes
    # system-prompt priming (~60s) makes the Aspire/Docker proxy time out the
    # upgrade (~10s), so Flutter sees "session ended" without ever getting ready.
    # After moshi handshakes, send a one-byte ready marker; Kernel waits for that
    # before telling Flutter the session is live.
    upstream_url = request.app["upstream_url"]
    client = web.WebSocketResponse(max_msg_size=PCM_FRAME_BYTES)
    await client.prepare(request)
    try:
        async with ClientSession() as session:
            async with session.ws_connect(upstream_url, max_msg_size=2 * 1024 * 1024) as upstream:
                handshake = await upstream.receive(timeout=UPSTREAM_HANDSHAKE_TIMEOUT_SECONDS)
                if handshake.type != WSMsgType.BINARY or handshake.data != b"\x00":
                    raise RuntimeError("official runtime did not complete its stream handshake")

                await client.send_bytes(b"\x00")

                opus_writer = sphn.OpusStreamWriter(PCM_SAMPLE_RATE)
                opus_reader = sphn.OpusStreamReader(PCM_SAMPLE_RATE)
                pending_pcm = np.empty(0, dtype=np.float32)
                input_frames: asyncio.Queue[bytes | None] = asyncio.Queue(MAX_BUFFERED_FRAMES)

                async def receive_pcm() -> None:
                    async for message in client:
                        if message.type != WSMsgType.BINARY:
                            raise ValueError("PersonaPlex adapter accepts binary PCM frames only.")
                        if message.data == b"\x00":
                            continue
                        _pcm16_to_float(message.data)
                        await input_frames.put(message.data)
                    await input_frames.put(None)

                async def send_to_upstream() -> None:
                    while (frame := await input_frames.get()) is not None:
                        opus_writer.append_pcm(_pcm16_to_float(frame))
                        opus = opus_writer.read_bytes()
                        if opus:
                            await upstream.send_bytes(b"\x01" + opus)

                async def receive_from_upstream() -> None:
                    nonlocal pending_pcm
                    async for message in upstream:
                        if message.type != WSMsgType.BINARY:
                            continue
                        data: bytes = message.data
                        if not data or data[0] != 1:
                            continue
                        opus_reader.append_bytes(data[1:])
                        decoded = opus_reader.read_pcm()
                        if decoded.shape[-1] == 0:
                            continue
                        pending_pcm = np.concatenate((pending_pcm, decoded))
                        while pending_pcm.shape[-1] >= PCM_FRAME_SAMPLES:
                            frame, pending_pcm = (
                                pending_pcm[:PCM_FRAME_SAMPLES],
                                pending_pcm[PCM_FRAME_SAMPLES:],
                            )
                            await client.send_bytes(_float_to_pcm16(frame))

                tasks = [
                    asyncio.create_task(receive_pcm()),
                    asyncio.create_task(send_to_upstream()),
                    asyncio.create_task(receive_from_upstream()),
                ]
                done, pending = await asyncio.wait(tasks, return_when=asyncio.FIRST_COMPLETED)
                for task in pending:
                    task.cancel()
                await asyncio.gather(*tasks, return_exceptions=True)
                for task in done:
                    task.result()
    except (ValueError, RuntimeError, asyncio.TimeoutError):
        if not client.closed:
            await client.close(code=1002, message=b"Invalid PersonaPlex stream.")
    except Exception:
        LOGGER.exception("PersonaPlex stream failed with a non-sensitive error type.")
        if not client.closed:
            await client.close(code=1011, message=b"PersonaPlex runtime unavailable.")
    return client


async def _warm_up(process: subprocess.Popen[object]) -> None:
    _require_dependencies()
    upstream_url = os.environ["PERSONAPLEX_UPSTREAM_URL"]
    deadline = asyncio.get_running_loop().time() + UPSTREAM_STARTUP_TIMEOUT_SECONDS
    while True:
        if process.poll() is not None:
            raise RuntimeError("official runtime exited during startup")
        remaining_seconds = deadline - asyncio.get_running_loop().time()
        if remaining_seconds <= 0:
            raise RuntimeError("official runtime did not become ready before the startup deadline")
        try:
            async with ClientSession() as session:
                async with session.ws_connect(
                    upstream_url,
                    max_msg_size=1024,
                    timeout=min(30, remaining_seconds),
                ) as upstream:
                    response = await upstream.receive(
                        timeout=min(UPSTREAM_HANDSHAKE_TIMEOUT_SECONDS, remaining_seconds)
                    )
                    if response.type == WSMsgType.BINARY and response.data == b"\x00":
                        return
                    LOGGER.warning(
                        "warmup_unexpected_message type=%s; retrying after backoff",
                        getattr(response, "type", "unknown"),
                    )
        except Exception:
            # The server has not bound its local socket yet, or priming is still
            # in progress; exit status is checked on the next iteration.
            await asyncio.sleep(2)
            continue
        await asyncio.sleep(2)


def _prefer_cpu_offload() -> bool:
    # Full BF16 PersonaPlex weights are ~16.7GB. On a 16GB card, full CUDA fills the
    # device, then the next session prime / restart hits cuDNN INTERNAL_ERROR / OOM.
    # Never escalate to full CUDA on small GPUs.
    try:
        import torch

        if not torch.cuda.is_available():
            return True
        total_bytes = torch.cuda.get_device_properties(0).total_memory
        return total_bytes < 22 * 1024**3
    except Exception:
        return True


async def _stop_process(process: subprocess.Popen[object]) -> None:
    if process.poll() is None:
        process.terminate()
        try:
            await asyncio.wait_for(asyncio.to_thread(process.wait), timeout=15)
        except asyncio.TimeoutError:
            process.kill()
            await asyncio.to_thread(process.wait)
    # Give the NVIDIA driver time to release the previous CUDA context.
    await asyncio.sleep(5)


async def supervise_runtime(app: web.Application) -> None:
    state: RuntimeState = app["runtime_state"]
    if not os.environ.get("HF_TOKEN"):
        state.set_failed("PersonaPlex runtime credentials are unavailable.")
        return
    if not app.get("adapter_token"):
        state.set_failed("PersonaPlex adapter credentials are unavailable.")
        return

    cpu_offload = _prefer_cpu_offload()
    environment = server_environment()
    consecutive_failures = 0
    while True:
        state.set_loading("Loading official PersonaPlex runtime.")
        process = subprocess.Popen(server_command(cpu_offload=cpu_offload), env=environment)
        try:
            await _warm_up(process)
            consecutive_failures = 0
            mode = "cpu-offload" if cpu_offload else "cuda"
            state.set_ready("Official PersonaPlex runtime is ready.", mode=mode)
            await asyncio.to_thread(process.wait)
            LOGGER.warning("official runtime exited; restarting")
            state.set_loading("Official PersonaPlex runtime stopped; restarting.")
            await _stop_process(process)
        except asyncio.CancelledError:
            await _stop_process(process)
            raise
        except Exception:
            consecutive_failures += 1
            await _stop_process(process)
            LOGGER.exception(
                "official runtime startup failed; retry=%s offload=%s",
                consecutive_failures,
                cpu_offload,
            )
            if consecutive_failures >= 6:
                state.set_failed("Official PersonaPlex runtime could not be started.")
                return
            await asyncio.sleep(min(30, 2 * consecutive_failures))
            continue
        await asyncio.sleep(2)


async def on_startup(app: web.Application) -> None:
    app["supervisor"] = asyncio.create_task(supervise_runtime(app))


async def on_cleanup(app: web.Application) -> None:
    supervisor: asyncio.Task[None] | None = app.get("supervisor")
    if supervisor is not None:
        supervisor.cancel()
        await asyncio.gather(supervisor, return_exceptions=True)


def create_app() -> web.Application:
    _require_dependencies()
    app = web.Application()
    app["runtime_state"] = RuntimeState()
    app["adapter_token"] = os.environ.get("PERSONAPLEX_ADAPTER_TOKEN")
    # Official NVIDIA voice pack uses names like NATF0.pt / NATM0.pt, not s0.pt.
    # Moshi requires a non-empty text_prompt; empty leaves text_prompt_tokens=None and
    # crashes warmup with TypeError during step_system_prompts_async.
    from urllib.parse import quote

    voice_prompt = os.environ.get("PERSONAPLEX_VOICE_PROMPT", "NATF0.pt")
    text_prompt = os.environ.get(
        "PERSONAPLEX_TEXT_PROMPT",
        "You are a friendly conversational assistant.",
    )
    app["upstream_url"] = (
        f"ws://{UPSTREAM_HOST}:{UPSTREAM_PORT}/api/chat"
        f"?text_prompt={quote(text_prompt)}&voice_prompt={quote(voice_prompt)}"
    )
    os.environ["PERSONAPLEX_UPSTREAM_URL"] = app["upstream_url"]
    app.router.add_get("/healthz", health_handler)
    app.router.add_get("/readyz", ready_handler)
    app.router.add_get("/stream", stream_handler)
    app.on_startup.append(on_startup)
    app.on_cleanup.append(on_cleanup)
    return app


if __name__ == "__main__":
    logging.basicConfig(level=os.environ.get("LOG_LEVEL", "INFO"))
    web.run_app(create_app(), host="0.0.0.0", port=8080)
