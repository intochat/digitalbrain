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

        self.assertEqual(command[:3], ["python", "-m", "moshi.server"])
        self.assertIn("--cpu-offload", command)
        self.assertEqual(command[command.index("--host") + 1], "127.0.0.1")
        self.assertEqual(command[command.index("--port") + 1], "8998")

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
    def test_image_is_digest_pinned_internal_only_and_has_locked_audio_dependency(self):
        root = ENTRYPOINT.parent
        dockerfile = (root / "Dockerfile").read_text(encoding="utf-8")
        requirements = (root / "requirements.lock").read_text(encoding="utf-8")

        self.assertIn("@sha256:", dockerfile)
        self.assertNotIn("EXPOSE", dockerfile)
        self.assertIn("sounddevice==0.5.1", requirements)
        self.assertIn("--no-deps /opt/personaplex/moshi", dockerfile)


if __name__ == "__main__":
    unittest.main()
