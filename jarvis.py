from __future__ import annotations

import argparse
import os
import platform
import sys
from pathlib import Path

from jarvis_app.actions import ActionEngine
from jarvis_app.assistant import AssistantService
from jarvis_app.codex_bridge import CodexBridge
from jarvis_app.config import SettingsStore
from jarvis_app.database import Database
from jarvis_app.voice import LocalVoiceRecognizer, TextToSpeech


PROJECT_DIR = Path(__file__).resolve().parent


def build_service(disable_codex: bool = False):
    store = SettingsStore()
    settings = store.load()
    if disable_codex:
        settings.codex_enabled = False
    database = Database(store.data_dir / "jarvis.db")
    actions = ActionEngine(database)
    codex = CodexBridge(settings, PROJECT_DIR) if settings.codex_enabled else None
    service = AssistantService(database, actions, codex)
    return service, settings, store


def diagnostics() -> int:
    service, settings, store = build_service(disable_codex=True)
    del service
    checks: list[tuple[str, str]] = [
        ("Python", sys.version.split()[0]),
        ("Windows", platform.platform()),
        ("Datos", str(store.data_dir)),
        ("Base SQLite", "correcta" if (store.data_dir / "jarvis.db").exists() else "no creada"),
        ("Modelo Codex", settings.codex_model),
        ("Sandbox Codex", "solo lectura"),
        ("API de pago", "bloqueada por diseño"),
    ]
    try:
        import openai_codex  # noqa: F401

        codex_status = "SDK instalado"
    except (ImportError, OSError) as exc:
        codex_status = f"no disponible ({type(exc).__name__})"
    checks.append(("Codex", codex_status))
    checks.append(
        (
            "Voz local",
            "dependencias instaladas" if LocalVoiceRecognizer.dependencies_available() else "ejecuta INSTALAR_VOZ.bat",
        )
    )
    key_present = any(os.environ.get(name) for name in ("OPENAI_API_KEY", "CODEX_API_KEY"))
    checks.append(
        (
            "Clave API encontrada en Windows",
            "sí, pero Jarvis la oculta y no la utiliza" if key_present else "no",
        )
    )
    print("\nDIAGNÓSTICO DE JARVIS\n" + "=" * 50)
    for name, value in checks:
        print(f"{name:30} {value}")
    print("=" * 50)
    return 0


def run_cli(service: AssistantService, tts: TextToSpeech) -> int:
    print("Jarvis listo. Escribe 'ayuda' o 'salir'.")
    while True:
        try:
            text = input("Tú > ").strip()
        except (EOFError, KeyboardInterrupt):
            print()
            break
        if text.casefold() in {"salir", "exit", "quit"}:
            break
        response, should_speak = service.process(text)
        print(f"Jarvis > {response}")
        if should_speak:
            tts.speak(response)
    tts.stop()
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Jarvis local con Codex")
    parser.add_argument("--cli", action="store_true", help="usar terminal en vez de interfaz gráfica")
    parser.add_argument("--diagnostico", action="store_true", help="comprobar instalación")
    parser.add_argument("--sin-codex", action="store_true", help="desactivar Codex para esta ejecución")
    parser.add_argument("--texto", help="procesar una sola orden y terminar")
    args = parser.parse_args()

    if args.diagnostico:
        return diagnostics()

    service, settings, store = build_service(disable_codex=args.sin_codex)
    tts = TextToSpeech(settings)
    if args.texto is not None:
        response, _should_speak = service.process(args.texto)
        print(response)
        return 0
    if args.cli:
        return run_cli(service, tts)

    from jarvis_app.gui import JarvisGUI

    voice = LocalVoiceRecognizer(settings)
    JarvisGUI(service, settings, voice, tts, store).run()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
