from __future__ import annotations

import threading
import tkinter as tk
from tkinter import ttk
from typing import TYPE_CHECKING

from .assistant import AssistantService
from .config import Settings, SettingsStore
from .models import VoiceUnavailable
from .voice import LocalVoiceRecognizer, TextToSpeech, WakeWordListener

if TYPE_CHECKING:
    from pystray import Icon


class JarvisGUI:
    BG = "#0b1020"
    PANEL = "#121a2f"
    INPUT = "#18233f"
    TEXT = "#e8eefc"
    MUTED = "#93a4c7"
    BLUE = "#4f8cff"
    GREEN = "#50d890"
    USER = "#b8ccff"

    def __init__(
        self,
        service: AssistantService,
        settings: Settings,
        voice: LocalVoiceRecognizer,
        tts: TextToSpeech,
        settings_store: SettingsStore | None = None,
    ) -> None:
        self.service = service
        self.settings = settings
        self.voice = voice
        self.tts = tts
        self.settings_store = settings_store
        self.wakeword = WakeWordListener(settings)
        self.tray_icon: Icon | None = None
        self.busy = False
        self.exiting = False

        self.root = tk.Tk()
        self.root.title("Jarvis Codex")
        self.root.geometry("860x650")
        self.root.minsize(680, 480)
        self.root.configure(bg=self.BG)
        self.root.protocol("WM_DELETE_WINDOW", self._hide_to_tray)

        self._configure_styles()
        self._build()
        self._start_tray()
        self._append(
            "Jarvis",
            "Listo. Las acciones locales no consumen cuota. Las preguntas complejas usan Terra en solo lectura. Escribe “ayuda” para comenzar.",
            "assistant",
        )
        self.entry.focus_set()
        if self.settings.wakeword_enabled:
            self.root.after(800, self._start_wakeword)

    def _configure_styles(self) -> None:
        style = ttk.Style(self.root)
        style.theme_use("clam")
        style.configure(
            "Jarvis.TButton",
            background=self.BLUE,
            foreground="white",
            borderwidth=0,
            padding=(16, 10),
            font=("Segoe UI Semibold", 10),
        )
        style.map("Jarvis.TButton", background=[("active", "#6b9eff"), ("disabled", "#38415a")])
        style.configure(
            "Voice.TButton",
            background=self.PANEL,
            foreground=self.TEXT,
            borderwidth=1,
            padding=(14, 10),
            font=("Segoe UI Semibold", 10),
        )
        style.map("Voice.TButton", background=[("active", self.INPUT), ("disabled", "#202942")])

    def _build(self) -> None:
        header = tk.Frame(self.root, bg=self.BG, padx=24, pady=18)
        header.pack(fill="x")
        tk.Label(
            header,
            text="JARVIS",
            bg=self.BG,
            fg=self.TEXT,
            font=("Segoe UI Semibold", 22),
        ).pack(side="left")
        tk.Label(
            header,
            text="LOCAL + CODEX",
            bg=self.PANEL,
            fg=self.GREEN,
            padx=10,
            pady=5,
            font=("Segoe UI Semibold", 9),
        ).pack(side="left", padx=12)
        tk.Label(
            header,
            text="Terra · sin API · solo lectura",
            bg=self.BG,
            fg=self.MUTED,
            font=("Segoe UI", 10),
        ).pack(side="right")

        chat_frame = tk.Frame(self.root, bg=self.PANEL, padx=16, pady=16)
        chat_frame.pack(fill="both", expand=True, padx=24, pady=(0, 14))
        self.chat = tk.Text(
            chat_frame,
            bg=self.PANEL,
            fg=self.TEXT,
            insertbackground=self.TEXT,
            relief="flat",
            wrap="word",
            state="disabled",
            font=("Segoe UI", 11),
            padx=8,
            pady=8,
            spacing1=3,
            spacing3=9,
        )
        scrollbar = ttk.Scrollbar(chat_frame, command=self.chat.yview)
        self.chat.configure(yscrollcommand=scrollbar.set)
        self.chat.pack(side="left", fill="both", expand=True)
        scrollbar.pack(side="right", fill="y")
        self.chat.tag_configure("assistant_name", foreground=self.GREEN, font=("Segoe UI Semibold", 10))
        self.chat.tag_configure("user_name", foreground=self.BLUE, font=("Segoe UI Semibold", 10))
        self.chat.tag_configure("assistant", foreground=self.TEXT, lmargin1=4, lmargin2=4)
        self.chat.tag_configure("user", foreground=self.USER, lmargin1=4, lmargin2=4)
        self.chat.tag_configure("error", foreground="#ff9b9b")

        controls = tk.Frame(self.root, bg=self.BG, padx=24)
        controls.pack(fill="x")
        self.entry = tk.Entry(
            controls,
            bg=self.INPUT,
            fg=self.TEXT,
            insertbackground=self.TEXT,
            relief="flat",
            font=("Segoe UI", 12),
        )
        self.entry.pack(side="left", fill="x", expand=True, ipady=11)
        self.entry.bind("<Return>", lambda _event: self._send())
        self.voice_button = ttk.Button(
            controls,
            text="🎙 Hablar",
            style="Voice.TButton",
            command=self._listen,
        )
        self.voice_button.pack(side="left", padx=(10, 0))
        self.send_button = ttk.Button(
            controls,
            text="Enviar",
            style="Jarvis.TButton",
            command=self._send,
        )
        self.send_button.pack(side="left", padx=(10, 0))

        footer = tk.Frame(self.root, bg=self.BG, padx=24, pady=12)
        footer.pack(fill="x")
        self.status = tk.StringVar(value="Listo")
        tk.Label(
            footer,
            textvariable=self.status,
            bg=self.BG,
            fg=self.MUTED,
            font=("Segoe UI", 9),
        ).pack(side="left")
        self.tts_var = tk.BooleanVar(value=self.settings.tts_enabled)
        tk.Checkbutton(
            footer,
            text="Leer respuestas",
            variable=self.tts_var,
            command=self._toggle_tts,
            bg=self.BG,
            fg=self.MUTED,
            activebackground=self.BG,
            activeforeground=self.TEXT,
            selectcolor=self.INPUT,
            font=("Segoe UI", 9),
        ).pack(side="right")
        self.wake_var = tk.BooleanVar(value=self.settings.wakeword_enabled)
        tk.Checkbutton(
            footer,
            text="Escuchar ‘Jarvis’",
            variable=self.wake_var,
            command=self._toggle_wakeword,
            bg=self.BG,
            fg=self.MUTED,
            activebackground=self.BG,
            activeforeground=self.TEXT,
            selectcolor=self.INPUT,
            font=("Segoe UI", 9),
        ).pack(side="right", padx=(0, 18))

    def _toggle_tts(self) -> None:
        self.settings.tts_enabled = bool(self.tts_var.get())
        self._save_settings()
        if not self.settings.tts_enabled:
            self.tts.stop()

    def _toggle_wakeword(self) -> None:
        self.settings.wakeword_enabled = bool(self.wake_var.get())
        self._save_settings()
        if self.settings.wakeword_enabled:
            self._start_wakeword()
        else:
            self.wakeword.stop()
            self.status.set("Activación por voz desactivada")

    def _save_settings(self) -> None:
        if self.settings_store is not None:
            try:
                self.settings_store.save(self.settings)
            except OSError:
                pass

    def _start_wakeword(self) -> None:
        if (
            not self.settings.wakeword_enabled
            or self.busy
            or self.exiting
            or self.tts.is_speaking()
            or self.wakeword.is_running()
        ):
            return
        try:
            self.wakeword.start(
                lambda score: self.root.after(0, lambda: self._wake_detected(score)),
                lambda message: self.root.after(0, lambda: self._wake_error(message)),
            )
            self.status.set("Escuchando ‘Jarvis’ localmente")
        except VoiceUnavailable as exc:
            self._wake_error(str(exc))

    def _wake_detected(self, score: float) -> None:
        if not self.settings.wakeword_enabled or self.busy:
            return
        self.status.set(f"Te escuché ({score:.0%}). Di tu orden…")
        self.root.deiconify()
        self.root.lift()
        self.root.after(180, self._listen)

    def _wake_error(self, message: str) -> None:
        self.settings.wakeword_enabled = False
        self.wake_var.set(False)
        self._save_settings()
        self._append("Jarvis", f"Activación por voz desactivada: {message}", "error")
        self.status.set("Activación por voz no disponible")

    def _restart_wake_when_ready(self) -> None:
        if not self.settings.wakeword_enabled or self.exiting:
            return
        if self.busy or self.tts.is_speaking():
            self.root.after(500, self._restart_wake_when_ready)
            return
        self._start_wakeword()

    def _set_busy(self, busy: bool, status: str = "Listo") -> None:
        self.busy = busy
        if busy:
            self.wakeword.stop()
        button_state = "disabled" if busy else "normal"
        self.send_button.configure(state=button_state)
        self.voice_button.configure(state=button_state)
        self.entry.configure(state=button_state)
        self.status.set(status)
        if not busy:
            self.entry.focus_set()

    def _send(self) -> None:
        if self.busy:
            return
        text = self.entry.get().strip()
        if not text:
            return
        self.entry.delete(0, "end")
        self._append("Tú", text, "user")
        self._set_busy(True, "Procesando…")
        threading.Thread(target=self._process_worker, args=(text,), daemon=True).start()

    def _process_worker(self, text: str) -> None:
        try:
            response, should_speak = self.service.process(text)
            self.root.after(0, lambda: self._finish_response(response, should_speak))
        except Exception as exc:
            message = f"Error inesperado: {exc}"
            self.root.after(0, lambda: self._finish_response(message, False, error=True))

    def _finish_response(self, response: str, should_speak: bool, error: bool = False) -> None:
        self._append("Jarvis", response, "error" if error else "assistant")
        self._set_busy(False)
        if should_speak and not error:
            self.tts.speak(response)
        self.root.after(600, self._restart_wake_when_ready)

    def _listen(self) -> None:
        if self.busy:
            return
        self.wakeword.stop()
        self._set_busy(True, "Preparando micrófono…")
        threading.Thread(target=self._listen_worker, daemon=True).start()

    def _listen_worker(self) -> None:
        try:
            text = self.voice.listen_once(
                lambda message: self.root.after(0, lambda m=message: self.status.set(m))
            )
            self.root.after(0, lambda: self._voice_received(text))
        except VoiceUnavailable as exc:
            message = str(exc)
            self.root.after(0, lambda: self._voice_error(message))
        except Exception as exc:
            message = f"Error de voz: {exc}"
            self.root.after(0, lambda: self._voice_error(message))

    def _voice_received(self, text: str) -> None:
        self._set_busy(False)
        self.entry.delete(0, "end")
        self.entry.insert(0, text)
        self._send()

    def _voice_error(self, message: str) -> None:
        self._append("Jarvis", message, "error")
        self._set_busy(False)
        self.root.after(600, self._restart_wake_when_ready)

    def _append(self, name: str, text: str, tag: str) -> None:
        self.chat.configure(state="normal")
        name_tag = "user_name" if name == "Tú" else "assistant_name"
        self.chat.insert("end", name + "\n", name_tag)
        self.chat.insert("end", text.strip() + "\n\n", tag)
        self.chat.configure(state="disabled")
        self.chat.see("end")

    def _start_tray(self) -> None:
        try:
            import pystray
            from PIL import Image, ImageDraw

            image = Image.new("RGBA", (64, 64), self.BG)
            draw = ImageDraw.Draw(image)
            draw.ellipse((5, 5, 59, 59), fill=self.BLUE, outline=self.GREEN, width=3)
            draw.text((24, 18), "J", fill="white")
            menu = pystray.Menu(
                pystray.MenuItem("Mostrar Jarvis", lambda _icon, _item: self.root.after(0, self._show)),
                pystray.MenuItem("Salir", lambda _icon, _item: self.root.after(0, self._close)),
            )
            self.tray_icon = pystray.Icon("JarvisCodex", image, "Jarvis Codex", menu)
            self.tray_icon.run_detached()
        except Exception:
            self.tray_icon = None

    def _show(self) -> None:
        self.root.deiconify()
        self.root.lift()
        self.root.focus_force()

    def _hide_to_tray(self) -> None:
        if self.tray_icon is None:
            self._close()
            return
        self.root.withdraw()

    def _close(self) -> None:
        self.exiting = True
        self.wakeword.stop()
        self.tts.stop()
        if self.tray_icon is not None:
            self.tray_icon.stop()
        self.root.destroy()

    def run(self) -> None:
        self.root.mainloop()
