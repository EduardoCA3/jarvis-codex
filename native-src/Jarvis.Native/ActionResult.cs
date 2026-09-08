namespace Jarvis.Native;

public sealed record ActionResult(bool Handled, string Message = "", bool Speak = true);
