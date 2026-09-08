from __future__ import annotations

from dataclasses import dataclass


@dataclass(slots=True)
class ActionResult:
    handled: bool
    text: str = ""
    speak: bool = True


class JarvisError(RuntimeError):
    """Error esperado que puede mostrarse al usuario."""


class CodexUnavailable(JarvisError):
    """Codex no está instalado, autenticado o disponible."""


class VoiceUnavailable(JarvisError):
    """La entrada de voz local no está disponible."""

