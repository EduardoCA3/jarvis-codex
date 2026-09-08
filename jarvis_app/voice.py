from __future__ import annotations

import base64
import ctypes
import os
import subprocess
import tempfile
import threading
import time
import wave
from pathlib import Path

from .config import Settings
from .models import VoiceUnavailable


class TextToSpeech:
    def __init__(self, settings: Settings) -> None:
        self.settings = settings
        self._process: subprocess.Popen[bytes] | None = None
        self._lock = threading.Lock()

    def speak(self, text: str) -> None:
        if not self.settings.tts_enabled or not text.strip():
            return
        threading.Thread(target=self._speak_worker, args=(text,), daemon=True).start()

    def _speak_worker(self, text: str) -> None:
        with self._lock:
            self.stop()
            script = (
                "Add-Type -AssemblyName System.Speech;"
                "$s=[System.Speech.Synthesis.SpeechSynthesizer]::new();"
                f"$s.Rate={self.settings.tts_rate};"
                "$v=$s.GetInstalledVoices() | Where-Object {$_.VoiceInfo.Culture.Name -like 'es-*'} | Select-Object -First 1;"
                "if($v){$s.SelectVoice($v.VoiceInfo.Name)};"
                "$s.Speak([Environment]::GetEnvironmentVariable('JARVIS_TTS_TEXT'));"
                "$s.Dispose();"
            )
            encoded = base64.b64encode(script.encode("utf-16le")).decode("ascii")
            env = os.environ.copy()
            env["JARVIS_TTS_TEXT"] = text[:4000]
            flags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
            try:
                self._process = subprocess.Popen(
                    [
                        os.path.join(
                            os.environ.get("WINDIR", r"C:\Windows"),
                            "System32",
                            "WindowsPowerShell",
                            "v1.0",
                            "powershell.exe",
                        ),
                        "-NoProfile",
                        "-EncodedCommand",
                        encoded,
                    ],
                    env=env,
                    stdin=subprocess.DEVNULL,
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                    creationflags=flags,
                )
                self._process.wait(timeout=90)
            except (OSError, subprocess.TimeoutExpired):
                if self._process:
                    self._process.kill()
            finally:
                self._process = None

    def stop(self) -> None:
        if self._process and self._process.poll() is None:
            self._process.terminate()

    def is_speaking(self) -> bool:
        return self._process is not None and self._process.poll() is None


class LocalVoiceRecognizer:
    SAMPLE_RATE = 16000
    CHANNELS = 1

    def __init__(self, settings: Settings) -> None:
        self.settings = settings
        self._model = None
        self._model_device = ""

    @staticmethod
    def dependencies_available() -> bool:
        try:
            import faster_whisper  # noqa: F401
            import numpy  # noqa: F401
            import sounddevice  # noqa: F401

            return True
        except (ImportError, OSError):
            return False

    def listen_once(self, status_callback=None) -> str:
        if not self.settings.voice_enabled:
            raise VoiceUnavailable("La entrada de voz está desactivada.")
        try:
            import numpy as np
            import sounddevice as sd
        except (ImportError, OSError) as exc:
            raise VoiceUnavailable(
                "Faltan los componentes de voz local. Ejecuta INSTALAR_VOZ.bat; no tiene coste."
            ) from exc

        if status_callback:
            status_callback("Escuchando… habla ahora")

        frames: list[object] = []
        finished = threading.Event()
        speech_started = False
        last_voice = time.monotonic()
        started = time.monotonic()
        threshold = 0.012

        def callback(indata, frame_count, time_info, status) -> None:
            nonlocal speech_started, last_voice
            del frame_count, time_info, status
            chunk = indata.copy()
            frames.append(chunk)
            level = float(np.sqrt(np.mean(np.square(chunk))))
            now = time.monotonic()
            if level >= threshold:
                speech_started = True
                last_voice = now
            if speech_started and now - last_voice >= self.settings.silence_seconds:
                finished.set()

        try:
            with sd.InputStream(
                samplerate=self.SAMPLE_RATE,
                channels=self.CHANNELS,
                dtype="float32",
                callback=callback,
                blocksize=1600,
            ):
                while not finished.wait(0.1):
                    if time.monotonic() - started >= self.settings.max_listen_seconds:
                        break
        except Exception as exc:
            raise VoiceUnavailable(f"No pude usar el micrófono: {exc}") from exc

        if not frames or not speech_started:
            raise VoiceUnavailable("No detecté voz. Acércate al micrófono e inténtalo otra vez.")

        audio = np.concatenate(frames, axis=0)
        audio = np.clip(audio * 32767, -32768, 32767).astype(np.int16)
        with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as temp:
            wav_path = Path(temp.name)
        try:
            with wave.open(str(wav_path), "wb") as wav:
                wav.setnchannels(self.CHANNELS)
                wav.setsampwidth(2)
                wav.setframerate(self.SAMPLE_RATE)
                wav.writeframes(audio.tobytes())
            if status_callback:
                status_callback("Transcribiendo localmente…")
            return self._transcribe(wav_path)
        finally:
            wav_path.unlink(missing_ok=True)

    def _load_model(self):
        if self._model is not None:
            return self._model
        try:
            from faster_whisper import WhisperModel
        except (ImportError, OSError) as exc:
            raise VoiceUnavailable("Faster-Whisper no está instalado correctamente.") from exc

        preferred = self.settings.whisper_device
        attempts = [preferred] if preferred != "auto" else (["cuda", "cpu"] if self._cuda_ready() else ["cpu"])
        errors: list[str] = []
        for device in attempts:
            compute = "float16" if device == "cuda" else "int8"
            try:
                self._model = WhisperModel(
                    self.settings.whisper_model,
                    device=device,
                    compute_type=compute,
                )
                self._model_device = device
                return self._model
            except Exception as exc:
                errors.append(f"{device}: {exc}")
        raise VoiceUnavailable("No pude cargar Whisper local. " + " | ".join(errors))

    @staticmethod
    def _cuda_ready() -> bool:
        if os.name != "nt":
            return True
        try:
            ctypes.WinDLL("cublas64_12.dll")
            return True
        except OSError:
            return False

    def _transcribe(self, wav_path: Path) -> str:
        model = self._load_model()
        try:
            text = self._run_transcription(model, wav_path)
        except Exception as exc:
            if self._model_device == "cuda" and self.settings.whisper_device == "auto":
                # CTranslate2 puede cargar el modelo antes de descubrir que faltan
                # las DLL de CUDA. Reintentamos de forma transparente en CPU.
                self._model = None
                self._model_device = ""
                original_device = self.settings.whisper_device
                self.settings.whisper_device = "cpu"
                try:
                    cpu_model = self._load_model()
                    text = self._run_transcription(cpu_model, wav_path)
                except Exception as cpu_exc:
                    raise VoiceUnavailable(
                        f"Falló la transcripción local en GPU y CPU: {cpu_exc}"
                    ) from cpu_exc
                finally:
                    self.settings.whisper_device = original_device
            else:
                raise VoiceUnavailable(f"Falló la transcripción local: {exc}") from exc
        if not text:
            raise VoiceUnavailable("No pude entender lo que dijiste.")
        return text

    @staticmethod
    def _run_transcription(model, wav_path: Path) -> str:
        segments, _info = model.transcribe(
            str(wav_path),
            language="es",
            beam_size=5,
            vad_filter=True,
        )
        # La inferencia de faster-whisper ocurre al consumir este generador.
        return " ".join(segment.text.strip() for segment in segments).strip()


class WakeWordListener:
    """Escucha ligera y local para la frase “Hey Jarvis”."""

    SAMPLE_RATE = 16000
    BLOCK_SIZE = 1280

    def __init__(self, settings: Settings) -> None:
        self.settings = settings
        self._thread: threading.Thread | None = None
        self._stop_event = threading.Event()
        self._model = None

    @staticmethod
    def dependencies_available() -> bool:
        try:
            import openwakeword  # noqa: F401
            import numpy  # noqa: F401
            import sounddevice  # noqa: F401

            return True
        except (ImportError, OSError):
            return False

    def is_running(self) -> bool:
        return self._thread is not None and self._thread.is_alive()

    def start(self, on_detected, on_error, on_ready=None) -> None:
        if self.is_running():
            return
        if not self.dependencies_available():
            raise VoiceUnavailable("Falta openWakeWord. Ejecuta INSTALAR_VOZ.bat.")
        self._stop_event.clear()
        self._thread = threading.Thread(
            target=self._run,
            args=(on_detected, on_error, on_ready),
            name="jarvis-wakeword",
            daemon=True,
        )
        self._thread.start()

    def stop(self) -> None:
        self._stop_event.set()

    def _ensure_model(self):
        if self._model is not None:
            self._model.reset()
            return self._model
        try:
            from openwakeword.model import Model
            from openwakeword.utils import download_models

            try:
                self._model = Model(
                    wakeword_models=["hey_jarvis"],
                    inference_framework="onnx",
                    vad_threshold=0.15,
                )
            except ValueError:
                download_models(["hey_jarvis"])
                self._model = Model(
                    wakeword_models=["hey_jarvis"],
                    inference_framework="onnx",
                    vad_threshold=0.15,
                )
            return self._model
        except Exception as exc:
            raise VoiceUnavailable(f"No pude cargar la palabra de activación: {exc}") from exc

    def _run(self, on_detected, on_error, on_ready=None) -> None:
        try:
            import numpy as np
            import sounddevice as sd

            model = self._ensure_model()
            with sd.RawInputStream(
                samplerate=self.SAMPLE_RATE,
                blocksize=self.BLOCK_SIZE,
                channels=1,
                dtype="int16",
            ) as stream:
                if on_ready:
                    on_ready()
                while not self._stop_event.is_set():
                    data, overflowed = stream.read(self.BLOCK_SIZE)
                    if overflowed:
                        continue
                    audio = np.frombuffer(data, dtype=np.int16)
                    prediction = model.predict(audio)
                    score = float(prediction.get("hey_jarvis", 0.0))
                    if score >= self.settings.wakeword_threshold:
                        self._stop_event.set()
                        on_detected(score)
                        return
        except Exception as exc:
            if not self._stop_event.is_set():
                on_error(str(exc))
        finally:
            self._thread = None
