from __future__ import annotations

import json
import os
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any


APP_NAME = "JarvisCodex"
ALLOWED_MODEL = "gpt-5.6-terra"


def default_data_dir() -> Path:
    override = os.environ.get("JARVIS_DATA_DIR")
    if override:
        return Path(override).expanduser().resolve()
    base = os.environ.get("LOCALAPPDATA")
    if base:
        return Path(base) / APP_NAME
    return Path.home() / f".{APP_NAME.lower()}"


@dataclass(slots=True)
class Settings:
    codex_enabled: bool = True
    codex_model: str = ALLOWED_MODEL
    tts_enabled: bool = True
    voice_enabled: bool = True
    wakeword_enabled: bool = False
    wakeword_threshold: float = 0.50
    whisper_model: str = "small"
    whisper_device: str = "auto"
    max_listen_seconds: int = 10
    silence_seconds: float = 1.2
    tts_rate: int = 0

    def validate(self) -> None:
        model = self.codex_model.strip().lower()
        if "luna" in model or model != ALLOWED_MODEL:
            raise ValueError(
                f"Modelo bloqueado: {self.codex_model!r}. Jarvis solo permite {ALLOWED_MODEL}."
            )
        if self.whisper_device not in {"auto", "cuda", "cpu"}:
            raise ValueError("whisper_device debe ser auto, cuda o cpu")
        self.max_listen_seconds = max(3, min(int(self.max_listen_seconds), 30))
        self.silence_seconds = max(0.6, min(float(self.silence_seconds), 4.0))
        self.wakeword_threshold = max(0.20, min(float(self.wakeword_threshold), 0.95))
        self.tts_rate = max(-5, min(int(self.tts_rate), 5))


class SettingsStore:
    def __init__(self, data_dir: Path | None = None) -> None:
        self.data_dir = data_dir or default_data_dir()
        self.path = self.data_dir / "config.json"

    def load(self) -> Settings:
        self.data_dir.mkdir(parents=True, exist_ok=True)
        if not self.path.exists():
            settings = Settings()
            self.save(settings)
            return settings

        try:
            raw: dict[str, Any] = json.loads(self.path.read_text(encoding="utf-8"))
            allowed = Settings.__dataclass_fields__.keys()
            settings = Settings(**{key: value for key, value in raw.items() if key in allowed})
            settings.validate()
            return settings
        except (OSError, ValueError, TypeError, json.JSONDecodeError) as exc:
            raise ValueError(f"Configuración inválida en {self.path}: {exc}") from exc

    def save(self, settings: Settings) -> None:
        settings.validate()
        self.data_dir.mkdir(parents=True, exist_ok=True)
        self.path.write_text(
            json.dumps(asdict(settings), ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
