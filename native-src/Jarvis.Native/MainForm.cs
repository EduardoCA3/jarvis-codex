using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Jarvis.Native;

public sealed class MainForm : Form
{
    private readonly TechConversation _conversation = new();
    private readonly TextBox _input = new();
    private readonly NeonButton _sendButton = new();
    private readonly TechStatusPill _status = new();
    private readonly TechToggle _continuousListening = new();
    private readonly TechToggle _aiMode = new();
    private readonly TechCoreControl _core = new();
    private readonly VoiceWaveControl _wave = new();
    private readonly Label _stateHeadline = new();
    private readonly Label _stateDetail = new();
    private readonly Label _brandName = new();
    private readonly Label _identityEyebrow = new();
    private readonly NotifyIcon _tray = new();
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly NativeActionEngine _actions;
    private readonly LocalStateStore _stateStore;
    private readonly SpeechService _speech = new();
    private string _assistantName;
    private RuntimePaths? _paths;
    private NativeVoiceService? _voice;
    private CodexAppServerClient? _codex;
    private bool _started;
    private bool _exitRequested;
    private bool _speechSettingsOpened;

    public MainForm()
    {
        _stateStore = new LocalStateStore();
        _assistantName = _stateStore.GetAssistantName();
        _actions = new NativeActionEngine(_stateStore);
        ConfigureWindow();
        BuildLayout();
        ConfigureTray();
        AddMessage(
            $"{_assistantName.ToUpperInvariant()} // CORE",
            $"Todos los sistemas están preparados. Di “{_assistantName}”, espera el tono y comunica tu orden.",
            TechTheme.Cyan);
    }

    private void ConfigureWindow()
    {
        Text = $"{_assistantName.ToUpperInvariant()} // Command Center";
        BackColor = TechTheme.Background;
        ForeColor = TechTheme.Text;
        Font = new Font("Segoe UI", 10F);
        Icon = SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 760);
        MinimumSize = new Size(1000, 680);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        FormClosing += OnFormClosing;
        Shown += OnShown;
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        try
        {
            var enabled = 1;
            DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
            var rounded = 2;
            DwmSetWindowAttribute(Handle, 33, ref rounded, sizeof(int));
            var backdrop = 2;
            DwmSetWindowAttribute(Handle, 38, ref backdrop, sizeof(int));
        }
        catch
        {
            // El diseño sigue funcionando en versiones de Windows sin estos efectos.
        }
    }

    private void BuildLayout()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(18),
            BackColor = TechTheme.Background
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 292));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(shell);

        shell.Controls.Add(BuildSidebar(), 0, 0);
        shell.Controls.Add(BuildCommandArea(), 1, 0);
    }

    private Control BuildSidebar()
    {
        var sidebar = new TechPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(20),
            CornerRadius = 22,
            FillColor = Color.FromArgb(11, 21, 38),
            FillColorBottom = Color.FromArgb(5, 12, 24),
            BorderColor = Color.FromArgb(42, 87, 113)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 224));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        sidebar.Controls.Add(layout);

        var brand = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        var mark = new Label
        {
            Text = "◈",
            ForeColor = TechTheme.Cyan,
            Font = new Font("Segoe UI Symbol", 24F),
            AutoSize = true,
            Location = new Point(0, 2)
        };
        _brandName.Text = _assistantName.ToUpperInvariant();
        _brandName.ForeColor = TechTheme.Text;
        _brandName.Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold);
        _brandName.AutoSize = false;
        _brandName.AutoEllipsis = true;
        _brandName.Size = new Size(142, 35);
        _brandName.Location = new Point(43, 1);
        var version = new Label
        {
            Text = "PERSONAL INTELLIGENCE  //  V1.0",
            ForeColor = TechTheme.Muted,
            Font = new Font("Consolas", 7.5F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(46, 36)
        };
        var editIdentity = new NeonButton
        {
            Text = "EDIT",
            Size = new Size(52, 27),
            Filled = false,
            Accent = TechTheme.Violet,
            SurfaceColor = Color.FromArgb(11, 21, 38),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        editIdentity.Click += (_, _) => _ = RenameAssistantAsync();
        brand.Resize += (_, _) => editIdentity.Location = new Point(Math.Max(185, brand.Width - editIdentity.Width), 3);
        brand.Controls.AddRange([mark, _brandName, version, editIdentity]);
        layout.Controls.Add(brand, 0, 0);

        _core.Dock = DockStyle.Fill;
        _core.Margin = new Padding(0, 0, 0, 2);
        layout.Controls.Add(_core, 0, 1);

        var state = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        _stateHeadline.Text = "VOICE LINK BOOTING";
        _stateHeadline.ForeColor = TechTheme.Cyan;
        _stateHeadline.Font = new Font("Consolas", 9.5F, FontStyle.Bold);
        _stateHeadline.AutoSize = false;
        _stateHeadline.TextAlign = ContentAlignment.MiddleCenter;
        _stateHeadline.Dock = DockStyle.Top;
        _stateHeadline.Height = 25;
        _stateDetail.Text = "Inicializando sistemas…";
        _stateDetail.ForeColor = TechTheme.Muted;
        _stateDetail.Font = new Font("Segoe UI", 8.5F);
        _stateDetail.AutoSize = false;
        _stateDetail.TextAlign = ContentAlignment.TopCenter;
        _stateDetail.Dock = DockStyle.Fill;
        state.Controls.Add(_stateDetail);
        state.Controls.Add(_stateHeadline);
        layout.Controls.Add(state, 0, 2);

        _wave.Dock = DockStyle.Fill;
        _wave.Margin = new Padding(8, 6, 8, 8);
        layout.Controls.Add(_wave, 0, 3);

        var badges = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        badges.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
        badges.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
        badges.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34F));
        badges.Controls.Add(CreateBadge("VOICE ENGINE", "WHISPER · LOCAL", TechTheme.Cyan), 0, 0);
        badges.Controls.Add(CreateBadge("AI MODEL", "CODEX TERRA", TechTheme.Violet), 0, 1);
        badges.Controls.Add(CreateBadge("SECURITY", "READ ONLY", TechTheme.Amber), 0, 2);
        layout.Controls.Add(badges, 0, 4);

        var telemetry = new Label
        {
            Dock = DockStyle.Fill,
            Text = "SYSTEM  ONLINE\r\nMIC LINK  ACTIVE\r\nAPI COST  $0.00",
            ForeColor = Color.FromArgb(83, 112, 140),
            Font = new Font("Consolas", 8F),
            TextAlign = ContentAlignment.BottomLeft,
            Padding = new Padding(5, 0, 0, 10)
        };
        layout.Controls.Add(telemetry, 0, 5);

        var quickArea = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        var quickTitle = new Label
        {
            Text = "QUICK ACCESS",
            ForeColor = TechTheme.Muted,
            Font = new Font("Consolas", 7.5F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(4, 1)
        };
        var quickButtons = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 55,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 0)
        };
        quickButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        quickButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        quickButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        quickButtons.Controls.Add(CreateQuickButton("AYUDA", "ayuda"), 0, 0);
        quickButtons.Controls.Add(CreateQuickButton("SISTEMA", "estado del sistema"), 1, 0);
        quickButtons.Controls.Add(CreateQuickButton("NOTAS", "mis notas"), 2, 0);
        quickArea.Controls.Add(quickTitle);
        quickArea.Controls.Add(quickButtons);
        layout.Controls.Add(quickArea, 0, 6);

        return sidebar;
    }

    private Control BuildCommandArea()
    {
        var area = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = TechTheme.Background,
            Margin = new Padding(0)
        };
        area.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        area.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        area.Controls.Add(BuildHeader(), 0, 0);

        var chatFrame = new TechPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(2),
            CornerRadius = 19,
            FillColor = TechTheme.Panel,
            FillColorBottom = TechTheme.Panel,
            BorderColor = Color.FromArgb(34, 72, 98)
        };
        _conversation.Dock = DockStyle.Fill;
        chatFrame.Controls.Add(_conversation);
        area.Controls.Add(chatFrame, 0, 1);
        area.Controls.Add(BuildComposer(), 0, 2);
        return area;
    }

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Fill, BackColor = TechTheme.Background };
        _identityEyebrow.Text = $"{_assistantName.ToUpperInvariant()} // PERSONAL AI SYSTEM";
        _identityEyebrow.ForeColor = TechTheme.Cyan;
        _identityEyebrow.Font = new Font("Consolas", 8.5F, FontStyle.Bold);
        _identityEyebrow.AutoSize = true;
        _identityEyebrow.Location = new Point(4, 6);
        var title = new Label
        {
            Text = "CENTRO DE COMANDO",
            ForeColor = TechTheme.Text,
            Font = new Font("Segoe UI Semibold", 23F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 27)
        };
        var subtitle = new Label
        {
            Text = "Control local · voz nativa · inteligencia conectada",
            ForeColor = TechTheme.Muted,
            Font = new Font("Segoe UI", 9F),
            AutoSize = true,
            Location = new Point(4, 67)
        };

        _status.StatusText = "INICIALIZANDO SISTEMAS";
        _status.Accent = TechTheme.Cyan;
        _status.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _continuousListening.Text = "Escucha continua activa";
        _continuousListening.Checked = true;
        _continuousListening.Enabled = false;
        _continuousListening.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _aiMode.Text = "Modo IA opcional";
        _aiMode.Checked = _stateStore.GetAiAssistantEnabled();
        _aiMode.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _aiMode.CheckedChanged += (_, _) =>
        {
            _stateStore.SetAiAssistantEnabled(_aiMode.Checked);
            AddMessage(
                "SYSTEM // AI MODE",
                _aiMode.Checked
                    ? "Modo IA activado. Las peticiones que no sean locales pueden consultar Codex."
                    : "Modo local activado. Codex solo se usará si se lo pides explícitamente.",
                _aiMode.Checked ? TechTheme.Violet : TechTheme.Cyan);
        };
        header.Resize += (_, _) =>
        {
            _status.Location = new Point(Math.Max(390, header.ClientSize.Width - _status.Width), 5);
            _aiMode.Location = new Point(
                Math.Max(500, header.ClientSize.Width - _aiMode.Width),
                26);
            _continuousListening.Location = new Point(
                Math.Max(500, header.ClientSize.Width - _continuousListening.Width),
                54);
        };
        header.Controls.AddRange([_identityEyebrow, title, subtitle, _status, _aiMode, _continuousListening]);
        return header;
    }

    private Control BuildComposer()
    {
        var frame = new TechPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(14, 12, 14, 12),
            CornerRadius = 17,
            FillColor = Color.FromArgb(12, 23, 40),
            FillColorBottom = Color.FromArgb(8, 17, 31),
            BorderColor = Color.FromArgb(42, 87, 113),
            AccentCorners = false
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));

        var prompt = new Label
        {
            Text = ">",
            Dock = DockStyle.Fill,
            ForeColor = TechTheme.Cyan,
            Font = new Font("Consolas", 15F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _input.Dock = DockStyle.Fill;
        _input.BackColor = Color.FromArgb(12, 23, 40);
        _input.ForeColor = TechTheme.Text;
        _input.BorderStyle = BorderStyle.None;
        _input.Font = new Font("Segoe UI", 12F);
        _input.PlaceholderText = $"Escribe una orden o pregunta para {_assistantName}…";
        _input.Margin = new Padding(0, 13, 10, 8);
        _input.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter && !eventArgs.Shift)
            {
                eventArgs.SuppressKeyPress = true;
                _ = SubmitTypedTextAsync();
            }
        };

        _sendButton.Text = "EJECUTAR  ›";
        _sendButton.Dock = DockStyle.Fill;
        _sendButton.Margin = new Padding(8, 4, 0, 4);
        _sendButton.Accent = TechTheme.Cyan;
        _sendButton.Filled = true;
        _sendButton.SurfaceColor = Color.FromArgb(12, 23, 40);
        _sendButton.Click += async (_, _) => await SubmitTypedTextAsync();
        layout.Controls.Add(prompt, 0, 0);
        layout.Controls.Add(_input, 1, 0);
        layout.Controls.Add(_sendButton, 2, 0);
        frame.Controls.Add(layout);
        return frame;
    }

    private static Control CreateBadge(string name, string value, Color accent)
    {
        var badge = new TechPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 0, 3),
            CornerRadius = 8,
            FillColor = Color.FromArgb(13, 27, 45),
            FillColorBottom = Color.FromArgb(9, 20, 35),
            BorderColor = Color.FromArgb(50, accent),
            AccentCorners = false
        };
        var nameLabel = new Label
        {
            Text = name,
            ForeColor = TechTheme.Muted,
            Font = new Font("Consolas", 7.3F),
            AutoSize = true,
            Location = new Point(10, 8)
        };
        var valueLabel = new Label
        {
            Text = value,
            ForeColor = accent,
            Font = new Font("Consolas", 7.8F, FontStyle.Bold),
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        badge.Resize += (_, _) => valueLabel.Location = new Point(Math.Max(95, badge.Width - valueLabel.Width - 10), 8);
        badge.Controls.AddRange([nameLabel, valueLabel]);
        return badge;
    }

    private NeonButton CreateQuickButton(string text, string command)
    {
        var button = new NeonButton
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(3),
            Filled = false,
            Accent = TechTheme.Cyan,
            SurfaceColor = Color.FromArgb(5, 12, 24)
        };
        button.Click += (_, _) => _ = HandleCommandAsync(command, false);
        return button;
    }

    private void ConfigureTray()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = TechTheme.PanelRaised,
            ForeColor = TechTheme.Text,
            ShowImageMargin = false
        };
        menu.Items.Add("Abrir centro de comando", null, (_, _) => ShowFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir del asistente", null, async (_, _) => await ExitAsync());
        _tray.Text = $"{_assistantName} Codex";
        _tray.Icon = SystemIcons.Application;
        _tray.Visible = true;
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowFromTray();
    }

    private async void OnShown(object? sender, EventArgs eventArgs)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        await StartVoiceAsync();
    }

    private async Task StartVoiceAsync()
    {
        VoiceDiagnostics.Write("main_voice_starting", $"name={_assistantName}");
        try
        {
            _voice = new NativeVoiceService(_assistantName);
            _voice.StatusChanged += text => Ui(() => SetStatus(text, TechTheme.Cyan));
            _voice.WakeDetected += _ => Ui(() => SetStatus("Activado: di tu orden", TechTheme.Amber));
            _voice.TranscriptReceived += text => Ui(() => _ = HandleCommandAsync(text, true));
            _voice.NoCommandRecognized += message => Ui(() =>
                SetStatus(message, TechTheme.Amber));
            _voice.Faulted += message => Ui(() =>
            {
                SetStatus("Problema con el micrófono", TechTheme.Red);
                AddMessage("VOICE // ERROR", message, TechTheme.Red);
            });
            await _voice.StartAsync(_lifetime.Token);
        }
        catch (Exception exception)
        {
            VoiceDiagnostics.Write("main_voice_start_error", $"0x{exception.HResult:X8}; {exception.Message}");
            SetStatus("Voz no disponible", TechTheme.Red);
            AddMessage("SYSTEM // ERROR", exception.Message, TechTheme.Red);
        }
    }

    private void OpenSpeechPrivacySettings()
    {
        if (_speechSettingsOpened)
        {
            return;
        }

        _speechSettingsOpened = true;
        AddMessage(
            "SYSTEM // PERMISSION",
            "El micrófono ya está permitido. Windows necesita que actives “Reconocimiento de voz en línea” para transcribir órdenes completas. Abrí la página correcta de Configuración.",
            TechTheme.Amber);
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:privacy-speech") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AddMessage("SYSTEM // ERROR", "No pude abrir Configuración: " + exception.Message, TechTheme.Red);
        }
    }

    private async void OnListeningChanged(object? sender, EventArgs eventArgs)
    {
        if (_voice is null)
        {
            return;
        }

        if (_continuousListening.Checked)
        {
            await _voice.ResumeAsync();
            SetStatus($"Escuchando \"{_assistantName}\"", TechTheme.Cyan);
        }
        else
        {
            await _voice.PauseAsync();
            SetStatus("Micrófono en pausa", TechTheme.Muted);
        }
    }

    private async Task SubmitTypedTextAsync()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        _input.Clear();
        await HandleCommandAsync(text, false);
    }

    private async Task RenameAssistantAsync()
    {
        using var dialog = new NameSettingsDialog(_assistantName);
        if (dialog.ShowDialog(this) != DialogResult.OK ||
            string.Equals(dialog.AssistantName, _assistantName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!await _commandLock.WaitAsync(0))
        {
            AddMessage(
                "SYSTEM // NOTICE",
                "Espera a que termine la solicitud actual antes de cambiar la identidad.",
                TechTheme.Amber);
            return;
        }

        try
        {
            var previousName = _assistantName;
            SetStatus("Reconfigurando identidad de voz…", TechTheme.Violet);
            if (_voice is not null)
            {
                try
                {
                    await _voice.PauseAsync();
                }
                catch
                {
                }

                _voice.Dispose();
                _voice = null;
            }

            _assistantName = dialog.AssistantName;
            _stateStore.SetAssistantName(_assistantName);
            UpdateIdentityUi();
            if (_codex is not null)
            {
                _codex.AssistantName = _assistantName;
            }

            AddMessage(
                $"{_assistantName.ToUpperInvariant()} // IDENTITY",
                $"Identidad actualizada. Ya no responderé a “{previousName}”; ahora me activarás diciendo “{_assistantName}”.",
                TechTheme.Violet);

            if (_continuousListening.Checked)
            {
                await StartVoiceAsync();
            }
            else
            {
                SetStatus("Micrófono en pausa", TechTheme.Muted);
            }
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private void UpdateIdentityUi()
    {
        Text = $"{_assistantName.ToUpperInvariant()} // Command Center";
        _brandName.Text = _assistantName.ToUpperInvariant();
        _identityEyebrow.Text = $"{_assistantName.ToUpperInvariant()} // PERSONAL AI SYSTEM";
        _input.PlaceholderText = $"Escribe una orden o pregunta para {_assistantName}…";
        _tray.Text = $"{_assistantName} Codex";
    }

    private async Task HandleCommandAsync(string text, bool fromVoice)
    {
        var endConversation = ConversationCommands.IsEnd(text, _assistantName);
        if (!await _commandLock.WaitAsync(0))
        {
            if (fromVoice && _voice is not null && _continuousListening.Checked)
            {
                await _voice.ResumeAsync();
            }

            return;
        }

        _sendButton.Enabled = false;
        _input.Enabled = false;
        try
        {
            if (endConversation)
            {
                _voice?.EndConversation();
            }

            if (_voice is not null)
            {
                await _voice.PauseAsync();
            }

            AddMessage(fromVoice ? "USER // VOICE" : "USER // INPUT", text, Color.FromArgb(128, 183, 255));
            SetStatus("Procesando solicitud…", TechTheme.Amber);

            var commandToExecute = endConversation ? "finalizar" : text;
            var action = await Task.Run(() => _actions.Execute(commandToExecute), _lifetime.Token);
            string answer;
            var shouldSpeak = true;
            if (action.Handled)
            {
                answer = action.Message;
                shouldSpeak = action.Speak;
            }
            else
            {
                var aiRequested = TryExtractExplicitAiRequest(text, out var aiQuestion);
                if (_aiMode.Checked || aiRequested)
                {
                    _paths ??= RuntimePaths.Discover();
                    _codex ??= new CodexAppServerClient(_paths);
                    _codex.AssistantName = _assistantName;
                    answer = await _codex.AskAsync(aiQuestion ?? text, _lifetime.Token);
                }
                else
                {
                    answer = "Estoy en modo local para ahorrar uso de IA. Puedo abrir aplicaciones, buscar archivos, controlar tu PC, guardar notas y consultar tus recuerdos. Si necesitas una respuesta de IA, di: consulta a Codex y luego tu pregunta.";
                }
            }

            AddMessage($"{_assistantName.ToUpperInvariant()} // RESPONSE", answer, TechTheme.Cyan);
            if (fromVoice && shouldSpeak)
            {
                SetStatus("Transmitiendo respuesta…", TechTheme.Violet);
                try
                {
                    await _speech.SpeakAsync(answer, _lifetime.Token);
                }
                catch (Exception exception)
                {
                    AddMessage("VOICE // WARNING", $"No pude leer la respuesta: {exception.Message}", TechTheme.Muted);
                }
            }
        }
        catch (Exception exception)
        {
            var message = FriendlyError(exception);
            AddMessage("SYSTEM // ERROR", message, TechTheme.Red);
            if (fromVoice)
            {
                try
                {
                    await _speech.SpeakAsync(message, _lifetime.Token);
                }
                catch
                {
                }
            }
        }
        finally
        {
            _sendButton.Enabled = true;
            _input.Enabled = true;
            _input.Focus();
            if (_voice is not null && _continuousListening.Checked)
            {
                if (fromVoice && !endConversation)
                {
                    _voice.KeepConversationOpen();
                }

                await _voice.ResumeAsync();
                SetStatus(
                    _voice.ConversationActive
                        ? "Conversación activa · puedes seguir hablando"
                        : $"Escuchando \"{_assistantName}\"",
                    TechTheme.Cyan);
            }
            else
            {
                SetStatus("Micrófono en pausa", TechTheme.Muted);
            }

            _commandLock.Release();
        }
    }

    private static bool TryExtractExplicitAiRequest(string text, out string? question)
    {
        var normalized = NativeActionEngine.Normalize(text).Trim();
        var prefixes = new[] { "consulta a codex", "consulta codex", "pregunta a codex", "pregunta a la ia", "consulta a la ia", "pregunta a ia", "consulta a ia" };
        foreach (var prefix in prefixes)
        {
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var remainder = text[prefix.Length..].TrimStart(' ', ',', ':', ';', '.', '¿', '?', '!');
            if (remainder.Length > 0)
            {
                question = remainder;
                return true;
            }
        }

        question = null;
        return false;
    }

    private static string FriendlyError(Exception exception)
    {
        var message = exception.Message;
        if (message.Contains("UsageLimitExceeded", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("usage limit", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("quota", StringComparison.OrdinalIgnoreCase))
        {
            return "Tu cuota incluida de Codex no está disponible ahora. No cambié a API ni a Luna, así que no habrá ningún cobro extra.";
        }

        if (message.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("login", StringComparison.OrdinalIgnoreCase))
        {
            return "Codex necesita que inicies sesión en la aplicación. La voz y las órdenes locales siguen funcionando gratis.";
        }

        return "No pude completar la petición: " + message;
    }

    private void AddMessage(string author, string text, Color color) =>
        _conversation.AddMessage(author, text, color);

    private void SetStatus(string text, Color color)
    {
        var normalized = NativeActionEngine.Normalize(text);
        var mode = CoreMode.Idle;
        var headline = "SYSTEM READY";
        if (normalized.Contains("problema") || normalized.Contains("no disponible") || normalized.Contains("fallo"))
        {
            mode = CoreMode.Error;
            headline = "VOICE LINK ERROR";
            color = TechTheme.Red;
        }
        else if (normalized.Contains("pausa"))
        {
            mode = CoreMode.Paused;
            headline = "MIC LINK PAUSED";
            color = TechTheme.Muted;
        }
        else if (normalized.Contains("procesando") ||
                 normalized.Contains("entendiendo") ||
                 normalized.Contains("verificando"))
        {
            mode = CoreMode.Processing;
            headline = normalized.Contains("procesando")
                ? "NEURAL PROCESSING"
                : "SPEECH DECODING";
            color = TechTheme.Amber;
        }
        else if (normalized.Contains("transmitiendo") || normalized.Contains("hablando"))
        {
            mode = CoreMode.Speaking;
            headline = "VOICE OUTPUT ACTIVE";
            color = TechTheme.Violet;
        }
        else if (normalized.Contains("tu orden") || normalized.Contains("activado"))
        {
            mode = CoreMode.Capturing;
            headline = "COMMAND CAPTURE ACTIVE";
            color = TechTheme.Amber;
        }
        else if (normalized.Contains("conversacion activa"))
        {
            mode = CoreMode.Listening;
            headline = "CONVERSATION ACTIVE";
            color = TechTheme.Cyan;
        }
        else if (normalized.Contains("escuchando"))
        {
            mode = CoreMode.Listening;
            headline = "VOICE LINK ACTIVE";
            color = TechTheme.Cyan;
        }

        _status.StatusText = text;
        _status.Accent = color;
        _core.Mode = mode;
        _wave.Active = mode is CoreMode.Listening or CoreMode.Capturing or CoreMode.Speaking;
        _wave.Accent = color;
        _stateHeadline.Text = headline;
        _stateHeadline.ForeColor = color;
        _stateDetail.Text = text;
    }

    private void Ui(Action action)
    {
        if (!IsDisposed && IsHandleCreated)
        {
            BeginInvoke(action);
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_exitRequested)
        {
            return;
        }

        eventArgs.Cancel = true;
        Hide();
        _tray.ShowBalloonTip(
            1800,
            $"{_assistantName} sigue activo",
            $"El enlace de voz continúa escuchando “{_assistantName}” desde la bandeja.",
            ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private async Task ExitAsync()
    {
        _exitRequested = true;
        _lifetime.Cancel();
        _voice?.Dispose();
        if (_codex is not null)
        {
            await _codex.DisposeAsync();
        }

        _tray.Visible = false;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _voice?.Dispose();
            _tray.Dispose();
            _lifetime.Dispose();
            _commandLock.Dispose();
        }

        base.Dispose(disposing);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
