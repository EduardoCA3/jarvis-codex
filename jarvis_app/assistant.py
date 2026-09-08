from __future__ import annotations

from .actions import ActionEngine
from .codex_bridge import CodexBridge
from .database import Database
from .models import CodexUnavailable


class AssistantService:
    def __init__(
        self,
        database: Database,
        actions: ActionEngine,
        codex: CodexBridge | None,
    ) -> None:
        self.database = database
        self.actions = actions
        self.codex = codex

    def process(self, text: str) -> tuple[str, bool]:
        clean = text.strip()
        if not clean:
            return "Escribe o di una orden.", False

        self.database.log_message("user", clean)
        result = self.actions.execute(clean)
        if result.handled:
            self.database.log_message("assistant", result.text)
            return result.text, result.speak

        if self.codex is None:
            response = (
                "No reconozco esa orden local y Codex está desactivado. "
                "Escribe ‘ayuda’ para ver los comandos disponibles."
            )
            self.database.log_message("assistant", response)
            return response, True

        try:
            response = self.codex.ask(clean)
        except CodexUnavailable as exc:
            response = str(exc)
        self.database.log_message("assistant", response)
        return response, True

