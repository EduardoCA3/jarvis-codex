from __future__ import annotations

import os
from contextlib import contextmanager
from pathlib import Path
from typing import Iterator

from .config import ALLOWED_MODEL, Settings
from .models import CodexUnavailable


SYSTEM_INSTRUCTIONS = """Eres el cerebro conversacional de un asistente personal llamado Jarvis.
Responde en español claro y breve. No afirmes haber ejecutado acciones en Windows.
No intentes modificar archivos ni configuraciones. Las acciones reales se gestionan
por un router local separado. Si el usuario pide algo peligroso, explica el riesgo.
No sugieras usar una API de pago, comprar créditos ni cambiar a Luna."""


@contextmanager
def without_paid_api_credentials() -> Iterator[None]:
    """Oculta credenciales de API dentro de este proceso y luego las restaura."""

    blocked = (
        "OPENAI_API_KEY",
        "CODEX_API_KEY",
        "OPENAI_BASE_URL",
        "OPENAI_ORG_ID",
        "OPENAI_PROJECT_ID",
    )
    saved = {name: os.environ.pop(name) for name in blocked if name in os.environ}
    try:
        yield
    finally:
        os.environ.update(saved)


class CodexBridge:
    def __init__(self, settings: Settings, workspace: Path) -> None:
        self.settings = settings
        self.workspace = workspace.resolve()

    def ask(self, prompt: str) -> str:
        if not self.settings.codex_enabled:
            raise CodexUnavailable("Codex está desactivado en la configuración.")
        if self.settings.codex_model != ALLOWED_MODEL or "luna" in self.settings.codex_model.lower():
            raise CodexUnavailable("El modelo configurado no está permitido.")

        try:
            from openai_codex import ApprovalMode, Codex, Sandbox
        except ImportError as exc:
            raise CodexUnavailable(
                "Falta el SDK gratuito de Codex. Ejecuta INSTALAR.bat y vuelve a intentarlo."
            ) from exc

        old_cwd = Path.cwd()
        try:
            os.chdir(self.workspace)
            with without_paid_api_credentials():
                with Codex() as codex:
                    thread = codex.thread_start(
                        approval_mode=ApprovalMode.deny_all,
                        cwd=str(self.workspace),
                        developer_instructions=SYSTEM_INSTRUCTIONS,
                        ephemeral=True,
                        model=ALLOWED_MODEL,
                        sandbox=Sandbox.read_only,
                    )
                    result = thread.run(prompt.strip())
            response = (result.final_response or "").strip()
            if not response:
                raise CodexUnavailable("Codex no devolvió una respuesta.")
            return response
        except CodexUnavailable:
            raise
        except Exception as exc:
            message = str(exc).strip()
            lowered = message.casefold()
            if any(word in lowered for word in ("limit", "quota", "usage", "credit")):
                raise CodexUnavailable(
                    "Se alcanzó el límite incluido de Codex. Jarvis se detuvo sin comprar créditos ni usar API."
                ) from exc
            if any(word in lowered for word in ("login", "sign in", "auth", "unauthorized")):
                raise CodexUnavailable(
                    "Codex necesita que inicies sesión con ChatGPT. Abre Codex, inicia sesión y vuelve a intentarlo."
                ) from exc
            raise CodexUnavailable(f"Codex no está disponible: {message or type(exc).__name__}") from exc
        finally:
            os.chdir(old_cwd)
