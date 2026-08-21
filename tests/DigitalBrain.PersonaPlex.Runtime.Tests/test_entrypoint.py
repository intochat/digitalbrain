import importlib.util
import logging
import os
import pathlib
import sys
import unittest
from unittest.mock import patch


ENTRYPOINT = (
    pathlib.Path(__file__).parents[2]
    / "src"
    / "Runtime"
    / "PersonaPlex"
    / "entrypoint.py"
)


def load_entrypoint():
    spec = importlib.util.spec_from_file_location("personaplex_entrypoint", ENTRYPOINT)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class RuntimeStateTests(unittest.TestCase):
    def test_health_and_ready_handlers_return_public_status_without_secrets(self):
        runtime = load_entrypoint()
        state = runtime.RuntimeState()
        request = _FakeRequest({"runtime_state": state}, headers={})

        with patch.object(runtime, "web", _FakeWeb):
            import asyncio

            health = asyncio.run(runtime.health_handler(request))
            not_ready = asyncio.run(runtime.ready_handler(request))
            state.set_ready("Official PersonaPlex runtime is ready.", mode="cuda")
            ready = asyncio.run(runtime.ready_handler(request))

        self.assertEqual(health.status, 200)
        self.assertEqual(not_ready.status, 503)
        self.assertEqual(ready.status, 200)
        self.assertNotIn("hf_test_token_123", str((health.payload, not_ready.payload, ready.payload)))

    def test_stream_requires_distinct_adapter_credential_without_leaking_hf_token(self):
        runtime = load_entrypoint()
        state = runtime.RuntimeState()
        state.set_ready("Official PersonaPlex runtime is ready.", mode="cuda")
        request = _FakeRequest(
            {"runtime_state": state, "adapter_token": "adapter-secret"}, headers={}
        )

        with patch.object(runtime, "web", _FakeWeb), patch.dict(
            os.environ, {"HF_TOKEN": "hf_test_token_123"}, clear=False
        ), self.assertLogs("personaplex-adapter", level=logging.WARNING) as logs:
            import asyncio

            response = asyncio.run(runtime.stream_handler(request))

        self.assertEqual(response.status, 401)
        self.assertEqual(response.payload, {"error": "unauthorized"})
        self.assertNotIn("hf_test_token_123", str((response.payload, logs.output)))
        self.assertIn("stream_authorization_failed reason=missing", logs.output[0])

    def test_stream_rejects_invalid_adapter_credential(self):
        runtime = load_entrypoint()
        state = runtime.RuntimeState()
        state.set_ready("Official PersonaPlex runtime is ready.", mode="cuda")
        request = _FakeRequest(
            {"runtime_state": state, "adapter_token": "adapter-secret"},
            headers={"Authorization": "Bearer wrong-secret"},
        )

        with patch.object(runtime, "web", _FakeWeb):
            import asyncio

            response = asyncio.run(runtime.stream_handler(request))

        self.assertEqual(response.status, 403)
        self.assertEqual(response.payload, {"error": "unauthorized"})
    def test_health_reports_each_public_readiness_state(self):
        runtime = load_entrypoint()
        state = runtime.RuntimeState()

        self.assertEqual(state.health()["state"], "downloading")
        state.set_loading("Loading official PersonaPlex runtime.")
        self.assertEqual(state.health()["state"], "loading")
        state.set_failed("Official PersonaPlex runtime could not be started.")
        self.assertEqual(state.health()["state"], "failed")
        state.set_ready("Official PersonaPlex runtime is ready.", mode="cpu-offload")
        self.assertEqual(
            state.health(),
            {
                "state": "ready",
                "mode": "cpu-offload",
                "message": "Official PersonaPlex runtime is ready.",
            },
        )

    def test_readiness_transition_emits_structured_safe_log(self):
        runtime = load_entrypoint()
        state = runtime.RuntimeState()

        with self.assertLogs("personaplex-adapter", level=logging.INFO) as logs:
            state.set_ready("Official PersonaPlex runtime is ready.", mode="cuda")

        self.assertIn("readiness_transition state=ready mode=cuda", logs.output[0])
        self.assertNotIn("HF_TOKEN", logs.output[0])

    def test_health_reports_readiness_without_secret_or_model_paths(self):
        runtime = load_entrypoint()
        state = runtime.RuntimeState()
        state.set_loading("Loading official PersonaPlex runtime.")
        state.set_ready("CUDA runtime ready.", mode="cuda")

        response = state.health("hf_secret_value", "/models/private-cache")

        self.assertEqual(
            response,
            {
                "state": "ready",
                "mode": "cuda",
                "message": "CUDA runtime ready.",
            },
        )
        self.assertNotIn("hf_secret_value", str(response))
        self.assertNotIn("/models/private-cache", str(response))

    def test_cuda_command_is_pinned_and_cpu_offload_is_explicit(self):
        runtime = load_entrypoint()
        command = runtime.server_command(cpu_offload=True)

        self.assertEqual(command[:3], ["python3", "-m", "moshi.server"])
        self.assertIn("--cpu-offload", command)
        self.assertEqual(command[command.index("--host") + 1], "127.0.0.1")
        self.assertEqual(command[command.index("--port") + 1], "8998")

    def test_cold_start_budget_allows_the_official_model_to_load(self):
        runtime = load_entrypoint()

        self.assertGreaterEqual(runtime.UPSTREAM_STARTUP_TIMEOUT_SECONDS, 300)

    def test_missing_hugging_face_token_reports_safe_failed_readiness(self):
        runtime = load_entrypoint()
        state = runtime.RuntimeState()
        app = {"runtime_state": state}

        with patch.dict(os.environ, {}, clear=True):
            import asyncio

            asyncio.run(runtime.supervise_runtime(app))

        self.assertEqual(
            state.health(),
            {
                "state": "failed",
                "mode": "unavailable",
                "message": "PersonaPlex runtime credentials are unavailable.",
            },
        )

    def test_pcm_protocol_requires_one_fixed_frame(self):
        runtime = load_entrypoint()

        self.assertIsNone(runtime.validate_pcm_frame(b"\x00" * 3840))
        with self.assertRaises(ValueError):
            runtime.validate_pcm_frame(b"\x00" * 3839)
        self.assertEqual(runtime.MAX_BUFFERED_FRAMES, 4)


class ContainerDefinitionTests(unittest.TestCase):
    def test_image_uses_locked_current_build_backend_before_installing_moshi(self):
        root = ENTRYPOINT.parent
        dockerfile = (root / "Dockerfile").read_text(encoding="utf-8")
        requirements = (root / "requirements.lock").read_text(encoding="utf-8")
        build_requirements_path = root / "build-requirements.lock"

        self.assertIn("@sha256:", dockerfile)
        self.assertNotIn("EXPOSE", dockerfile)
        self.assertIn("sounddevice==0.5.1", requirements)
        self.assertIn("--no-deps --no-build-isolation /opt/personaplex/moshi", dockerfile)
        self.assertIn("--require-hashes -r requirements.lock", dockerfile)
        self.assertIn("--require-hashes -r build-requirements.lock", dockerfile)
        self.assertTrue(build_requirements_path.is_file())
        build_requirements = build_requirements_path.read_text(encoding="utf-8")
        self.assertIn("setuptools==", build_requirements)
        self.assertIn("wheel==", build_requirements)
        self.assertIn("--hash=sha256:", build_requirements)
        self.assertIn("snapshot.ubuntu.com", dockerfile)
        self.assertIn("python3-setuptools", dockerfile)
        self.assertTrue((root / "uv.lock").is_file())
        self.assertIn("--hash=sha256:", requirements)

    def test_task2_adapter_secret_contract_keeps_hf_token_runtime_only(self):
        contract = (ENTRYPOINT.parent / "README.md").read_text(encoding="utf-8")

        self.assertIn("PERSONAPLEX_ADAPTER_TOKEN", contract)
        self.assertIn("Kernel and runtime", contract)
        self.assertIn("HF_TOKEN", contract)
        self.assertIn("runtime only", contract)


class _FakeResponse:
    def __init__(self, payload, status=200):
        self.payload = payload
        self.status = status


class _FakeWeb:
    @staticmethod
    def json_response(payload, status=200):
        return _FakeResponse(payload, status)


class _FakeRequest(dict):
    def __init__(self, app, *, headers):
        super().__init__(app)
        self.app = app
        self.headers = headers


if __name__ == "__main__":
    unittest.main()
