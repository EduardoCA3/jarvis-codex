namespace Jarvis.Native;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var singleInstance = new Mutex(
            initiallyOwned: true,
            name: @"Local\JarvisCodexNative.Singleton",
            createdNew: out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Jarvis ya está ejecutándose. Busca su icono junto al reloj de Windows.",
                "Jarvis",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.Run(new MainForm());
        GC.KeepAlive(singleInstance);
    }
}
