"""
Python startup shim (auto-imported by `site`) to keep VibeVoice compatible on
Windows + CPU-only PyTorch installs.

Some `diffusers` versions reference `torch.xpu.*` unconditionally, but the
CPU-only PyTorch wheels often don't expose `torch.xpu`, which crashes imports
before VibeVoice can start.

Usage (PowerShell):
  $env:PYTHONPATH = "C:\\Users\\Carlos Ivan\\Desktop\\Agente\\Plataforma\\tts\\pyshim"
  python demo\\realtime_model_inference_from_file.py ...
"""

from __future__ import annotations


def _install_torch_xpu_shim() -> None:
    try:
        import torch
    except Exception:
        return

    if hasattr(torch, "xpu"):
        return

    class _DummyXPU:
        @staticmethod
        def is_available() -> bool:
            return False

        @staticmethod
        def empty_cache() -> None:
            return None

        @staticmethod
        def device_count() -> int:
            return 0

        @staticmethod
        def manual_seed(seed: int):
            return torch.manual_seed(seed)

        @staticmethod
        def reset_peak_memory_stats(*_args, **_kwargs) -> None:
            return None

        @staticmethod
        def max_memory_allocated(*_args, **_kwargs) -> int:
            return 0

        @staticmethod
        def synchronize(*_args, **_kwargs) -> None:
            return None

    torch.xpu = _DummyXPU()  # type: ignore[attr-defined]


_install_torch_xpu_shim()

