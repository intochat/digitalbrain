import importlib.util
import pathlib
import sys
import unittest


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


if __name__ == "__main__":
    unittest.main()
