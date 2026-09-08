from __future__ import annotations

import ctypes
import os
import platform
import re
import shutil
import subprocess
import unicodedata
import urllib.parse
import webbrowser
from datetime import datetime
from pathlib import Path

from .database import Database
from .models import ActionResult


def normalize(text: str) -> str:
    decomposed = unicodedata.normalize("NFKD", text.casefold().strip())
    return "".join(char for char in decomposed if not unicodedata.combining(char))


HELP_TEXT = """Puedo ejecutar estas órdenes localmente:
• qué hora es / qué fecha es
• abre bloc de notas, calculadora o explorador
• abre Google, YouTube o Gmail
• busca <algo>
• anota <texto> / mis notas
• recuerda que <dato> / qué recuerdas
• sube volumen / baja volumen / silencio / pausa
• estado del sistema

Para una pregunta normal usaré Codex Terra en modo de solo lectura."""


class ActionEngine:
    APPS: dict[str, list[str]] = {
        "bloc de notas": ["notepad.exe"],
        "notepad": ["notepad.exe"],
        "calculadora": ["calc.exe"],
        "explorador": ["explorer.exe"],
        "administrador de tareas": ["taskmgr.exe"],
    }
    SITES: dict[str, str] = {
        "google": "https://www.google.com/",
        "youtube": "https://www.youtube.com/",
        "gmail": "https://mail.google.com/",
        "chatgpt": "https://chatgpt.com/",
    }
    BLOCKED_ACTION_PREFIXES = (
        "borra ",
        "elimina ",
        "formatea ",
        "instala ",
        "desinstala ",
        "compra ",
        "paga ",
        "envia ",
        "manda un mensaje",
        "apaga ",
        "reinicia ",
    )

    def __init__(self, database: Database) -> None:
        self.database = database

    def execute(self, original: str) -> ActionResult:
        clean = original.strip()
        text = normalize(clean)
        if not text:
            return ActionResult(True, "No escuché ninguna orden.", False)

        if any(text.startswith(prefix) for prefix in self.BLOCKED_ACTION_PREFIXES):
            return ActionResult(
                True,
                "Esa acción está bloqueada por seguridad. Esta versión no borra, instala, compra, envía ni cambia el sistema automáticamente.",
            )

        if text in {"ayuda", "comandos", "que puedes hacer", "qué puedes hacer"}:
            return ActionResult(True, HELP_TEXT, False)

        if text in {"hora", "que hora es", "dime la hora"}:
            now = datetime.now()
            return ActionResult(True, f"Son las {now:%H:%M}.")

        if text in {"fecha", "que fecha es", "que dia es", "dime la fecha"}:
            days = ("lunes", "martes", "miércoles", "jueves", "viernes", "sábado", "domingo")
            months = (
                "enero", "febrero", "marzo", "abril", "mayo", "junio",
                "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre",
            )
            now = datetime.now()
            return ActionResult(
                True,
                f"Hoy es {days[now.weekday()]} {now.day} de {months[now.month - 1]} de {now.year}.",
            )

        if text.startswith("abre "):
            target = text.removeprefix("abre ").strip()
            return self._open_target(target)

        search = re.match(r"^(?:busca|buscar)\s+(.+)$", clean, flags=re.IGNORECASE)
        if search:
            query = search.group(1).strip()
            webbrowser.open("https://www.google.com/search?q=" + urllib.parse.quote_plus(query))
            return ActionResult(True, f"Buscando {query} en tu navegador.")

        note = re.match(r"^(?:anota|apunta|nota)\s+(.+)$", clean, flags=re.IGNORECASE)
        if note:
            content = note.group(1).strip()
            note_id = self.database.add_note(content)
            return ActionResult(True, f"Nota {note_id} guardada localmente: {content}")

        if text in {"mis notas", "muestra mis notas", "lista mis notas"}:
            notes = self.database.list_notes()
            if not notes:
                return ActionResult(True, "Todavía no tienes notas guardadas.")
            lines = [f"{row['id']}. {row['content']}" for row in notes]
            return ActionResult(True, "Tus últimas notas:\n" + "\n".join(lines), False)

        memory = re.match(r"^recuerda(?: que)?\s+(.+)$", clean, flags=re.IGNORECASE)
        if memory:
            content = memory.group(1).strip()
            added = self.database.add_memory(content)
            message = "Lo recordaré localmente" if added else "Ya tenía guardado ese dato"
            return ActionResult(True, f"{message}: {content}")

        if text in {"que recuerdas", "que sabes de mi", "muestra tus recuerdos"}:
            memories = self.database.list_memories()
            if not memories:
                return ActionResult(True, "Todavía no me has pedido recordar nada.")
            lines = [f"• {row['content']}" for row in memories]
            return ActionResult(True, "Recuerdo esto:\n" + "\n".join(lines), False)

        media = self._media_action(text)
        if media is not None:
            return media

        if text in {"estado del sistema", "estado del pc", "como esta mi pc", "sistema"}:
            return self._system_status()

        return ActionResult(False)

    def _open_target(self, target: str) -> ActionResult:
        if target in self.APPS:
            subprocess.Popen(
                self.APPS[target],
                stdin=subprocess.DEVNULL,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                shell=False,
            )
            return ActionResult(True, f"Abriendo {target}.")
        if target in self.SITES:
            webbrowser.open(self.SITES[target])
            return ActionResult(True, f"Abriendo {target} en tu navegador.")
        return ActionResult(
            True,
            "Por seguridad solo puedo abrir: bloc de notas, calculadora, explorador, administrador de tareas, Google, YouTube, Gmail o ChatGPT.",
        )

    @staticmethod
    def _press_media_key(virtual_key: int, count: int = 1) -> None:
        key_up = 0x0002
        for _ in range(count):
            ctypes.windll.user32.keybd_event(virtual_key, 0, 0, 0)
            ctypes.windll.user32.keybd_event(virtual_key, 0, key_up, 0)

    def _media_action(self, text: str) -> ActionResult | None:
        if text in {"sube el volumen", "subir volumen", "volumen arriba"}:
            self._press_media_key(0xAF, 3)
            return ActionResult(True, "Subí el volumen.")
        if text in {"baja el volumen", "bajar volumen", "volumen abajo"}:
            self._press_media_key(0xAE, 3)
            return ActionResult(True, "Bajé el volumen.")
        if text in {"silencio", "silenciar", "quita el sonido"}:
            self._press_media_key(0xAD)
            return ActionResult(True, "Cambié el estado de silencio.")
        if text in {"pausa", "continuar musica", "reproducir", "play"}:
            self._press_media_key(0xB3)
            return ActionResult(True, "Cambié la reproducción.")
        return None

    @staticmethod
    def _system_status() -> ActionResult:
        root = Path(os.environ.get("SystemDrive", "C:") + os.sep)
        disk = shutil.disk_usage(root)
        parts = [
            f"Windows en {platform.machine()}",
            f"disco C con {disk.free / (1024**3):.1f} GB libres",
        ]
        try:
            import psutil

            memory = psutil.virtual_memory()
            parts.append(
                f"RAM al {memory.percent:.0f} por ciento, con {memory.available / (1024**3):.1f} GB disponibles"
            )
            parts.append(f"CPU al {psutil.cpu_percent(interval=0.2):.0f} por ciento")
        except ImportError:
            parts.append("instala psutil para ver RAM y CPU")
        return ActionResult(True, "; ".join(parts) + ".")

