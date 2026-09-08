from __future__ import annotations

import sqlite3
from contextlib import contextmanager
from datetime import datetime
from pathlib import Path
from typing import Iterator


class Database:
    def __init__(self, path: Path) -> None:
        self.path = path
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._initialize()

    @contextmanager
    def _connect(self) -> Iterator[sqlite3.Connection]:
        connection = sqlite3.connect(self.path, timeout=10)
        connection.row_factory = sqlite3.Row
        try:
            yield connection
            connection.commit()
        except Exception:
            connection.rollback()
            raise
        finally:
            connection.close()

    def _initialize(self) -> None:
        with self._connect() as db:
            db.executescript(
                """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS notes (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    content TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS memories (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    content TEXT NOT NULL UNIQUE,
                    created_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS messages (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    role TEXT NOT NULL CHECK(role IN ('user', 'assistant', 'system')),
                    content TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                """
            )

    @staticmethod
    def _now() -> str:
        return datetime.now().astimezone().isoformat(timespec="seconds")

    def add_note(self, content: str) -> int:
        with self._connect() as db:
            cursor = db.execute(
                "INSERT INTO notes(content, created_at) VALUES (?, ?)",
                (content.strip(), self._now()),
            )
            return int(cursor.lastrowid)

    def list_notes(self, limit: int = 10) -> list[sqlite3.Row]:
        with self._connect() as db:
            return list(
                db.execute(
                    "SELECT id, content, created_at FROM notes ORDER BY id DESC LIMIT ?",
                    (max(1, min(limit, 100)),),
                )
            )

    def add_memory(self, content: str) -> bool:
        try:
            with self._connect() as db:
                db.execute(
                    "INSERT INTO memories(content, created_at) VALUES (?, ?)",
                    (content.strip(), self._now()),
                )
            return True
        except sqlite3.IntegrityError:
            return False

    def list_memories(self, limit: int = 20) -> list[sqlite3.Row]:
        with self._connect() as db:
            return list(
                db.execute(
                    "SELECT id, content, created_at FROM memories ORDER BY id DESC LIMIT ?",
                    (max(1, min(limit, 100)),),
                )
            )

    def log_message(self, role: str, content: str) -> None:
        if role not in {"user", "assistant", "system"}:
            raise ValueError("Rol de mensaje inválido")
        with self._connect() as db:
            db.execute(
                "INSERT INTO messages(role, content, created_at) VALUES (?, ?, ?)",
                (role, content.strip(), self._now()),
            )
