namespace Jarvis.Native;

public sealed class NameSettingsDialog : Form
{
    private readonly TextBox _nameInput = new();
    private readonly Label _preview = new();
    private readonly Label _error = new();

    public string AssistantName { get; private set; }

    public NameSettingsDialog(string currentName)
    {
        AssistantName = AssistantIdentity.Validate(currentName);
        ConfigureWindow();
        BuildLayout();
    }

    private void ConfigureWindow()
    {
        Text = "Identidad del asistente";
        BackColor = TechTheme.Background;
        ForeColor = TechTheme.Text;
        Font = new Font("Segoe UI", 10F);
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 330);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Icon = SystemIcons.Application;
    }

    private void BuildLayout()
    {
        var frame = new TechPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(30),
            CornerRadius = 0,
            FillColor = TechTheme.Background,
            FillColorBottom = Color.FromArgb(7, 14, 27),
            BorderColor = TechTheme.Background,
            AccentCorners = false
        };
        Controls.Add(frame);

        var eyebrow = new Label
        {
            Text = "IDENTITY CONFIGURATION",
            ForeColor = TechTheme.Cyan,
            Font = new Font("Consolas", 8.5F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(30, 25)
        };
        var title = new Label
        {
            Text = "Elige cómo llamarlo",
            ForeColor = TechTheme.Text,
            Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(26, 49)
        };
        var explanation = new Label
        {
            Text = "El nombre se guarda en tu PC. El asistente dejará de responder al nombre anterior.",
            ForeColor = TechTheme.Muted,
            Font = new Font("Segoe UI", 9F),
            AutoSize = false,
            Size = new Size(410, 42),
            Location = new Point(30, 91)
        };

        var inputFrame = new TechPanel
        {
            Location = new Point(30, 137),
            Size = new Size(420, 55),
            Padding = new Padding(15, 15, 15, 8),
            CornerRadius = 12,
            FillColor = Color.FromArgb(14, 27, 45),
            FillColorBottom = Color.FromArgb(10, 20, 36),
            BorderColor = Color.FromArgb(75, TechTheme.Cyan),
            AccentCorners = false
        };
        _nameInput.Dock = DockStyle.Fill;
        _nameInput.BorderStyle = BorderStyle.None;
        _nameInput.BackColor = Color.FromArgb(14, 27, 45);
        _nameInput.ForeColor = TechTheme.Text;
        _nameInput.Font = new Font("Segoe UI Semibold", 12F);
        _nameInput.Text = AssistantName;
        _nameInput.MaxLength = 20;
        _nameInput.TextChanged += (_, _) => UpdatePreview();
        inputFrame.Controls.Add(_nameInput);

        _preview.ForeColor = TechTheme.CyanSoft;
        _preview.Font = new Font("Consolas", 8.5F);
        _preview.AutoSize = false;
        _preview.Size = new Size(420, 24);
        _preview.Location = new Point(31, 202);
        _error.ForeColor = TechTheme.Red;
        _error.Font = new Font("Segoe UI", 8.5F);
        _error.AutoSize = false;
        _error.Size = new Size(420, 22);
        _error.Location = new Point(31, 225);

        var cancel = new NeonButton
        {
            Text = "CANCELAR",
            Location = new Point(214, 264),
            Size = new Size(110, 40),
            Filled = false,
            Accent = TechTheme.Muted,
            SurfaceColor = Color.FromArgb(7, 14, 27),
            DialogResult = DialogResult.Cancel
        };
        var save = new NeonButton
        {
            Text = "GUARDAR",
            Location = new Point(335, 264),
            Size = new Size(115, 40),
            Filled = true,
            Accent = TechTheme.Cyan,
            SurfaceColor = Color.FromArgb(7, 14, 27)
        };
        save.Click += (_, _) => SaveName();
        AcceptButton = save;
        CancelButton = cancel;
        frame.Controls.AddRange([eyebrow, title, explanation, inputFrame, _preview, _error, cancel, save]);
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var value = _nameInput.Text.Trim();
        _preview.Text = value.Length == 0
            ? "WAKE PHRASE  //  —"
            : $"WAKE PHRASE  //  “{value}”";
        _error.Text = "";
    }

    private void SaveName()
    {
        try
        {
            AssistantName = AssistantIdentity.Validate(_nameInput.Text);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (ArgumentException exception)
        {
            _error.Text = exception.Message;
            _nameInput.Focus();
        }
    }
}
