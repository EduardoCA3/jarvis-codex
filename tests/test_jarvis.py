from __future__ import annotations

import tempfile
import unittest
import os
from pathlib import Path

from jarvis_app.actions import ActionEngine
from jarvis_app.assistant import AssistantService
from jarvis_app.config import Settings
from jarvis_app.codex_bridge import without_paid_api_credentials
from jarvis_app.database import Database


class JarvisTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.db = Database(Path(self.temp.name) / "test.db")
        self.actions = ActionEngine(self.db)
        self.service = AssistantService(self.db, self.actions, None)

    def tearDown(self) -> None:
        self.temp.cleanup()

    def test_help_is_local(self) -> None:
        response, speak = self.service.process("ayuda")
        self.assertIn("órdenes", response)
        self.assertFalse(speak)

    def test_notes_round_trip(self) -> None:
        response, _ = self.service.process("anota comprar leche")
        self.assertIn("guardada", response)
        listed, _ = self.service.process("mis notas")
        self.assertIn("comprar leche", listed)

    def test_memories_are_local_and_unique(self) -> None:
        self.service.process("recuerda que prefiero respuestas breves")
        self.service.process("recuerda que prefiero respuestas breves")
        memories = self.db.list_memories()
        self.assertEqual(len(memories), 1)

    def test_dangerous_action_is_blocked(self) -> None:
        response, _ = self.service.process("borra todos mis archivos")
        self.assertIn("bloqueada", response)

    def test_unknown_command_does_not_execute_without_codex(self) -> None:
        response, _ = self.service.process("haz algo desconocido")
        self.assertIn("Codex está desactivado", response)

    def test_luna_is_rejected(self) -> None:
        settings = Settings(codex_model="gpt-5.6-luna")
        with self.assertRaises(ValueError):
            settings.validate()

    def test_api_key_is_hidden_and_restored(self) -> None:
        original = os.environ.get("OPENAI_API_KEY")
        os.environ["OPENAI_API_KEY"] = "test-key-that-is-never-sent"
        try:
            with without_paid_api_credentials():
                self.assertNotIn("OPENAI_API_KEY", os.environ)
            self.assertEqual(os.environ.get("OPENAI_API_KEY"), "test-key-that-is-never-sent")
        finally:
            if original is None:
                os.environ.pop("OPENAI_API_KEY", None)
            else:
                os.environ["OPENAI_API_KEY"] = original


if __name__ == "__main__":
    unittest.main()
