using System.Text;
using IrisXe.Thermal.Core;
using IrisXe.Thermal.Core.Access;
using IrisXe.Thermal.Core.Decoding;
using IrisXe.Thermal.Core.Diagnostics;
using IrisXe.Thermal.Core.Export;
using IrisXe.Thermal.Core.Hardware;
using IrisXe.Thermal.Core.Security;
using Microsoft.Win32;

namespace IrisXe.Thermal.App;

/// <summary>
/// Janela principal (PT-BR). FASE 1 — sem driver e sem qualquer acesso privilegiado:
/// o mecanismo de leitura ativo e "Indisponivel" ou "Valores manuais (SIMULADO)".
/// Todas as operacoes sao estritamente somente leitura e todas as leituras sao auditadas.
/// </summary>
public sealed class MainForm : Form
{
    private readonly AuditLog _audit = new();
    private readonly UnavailableMsrReader _unavailable = new();
    private readonly ManualMsrReader _manual = new();
    private readonly DiagnosticEngine _engine;
    private readonly System.Windows.Forms.Timer _sampleTimer = new() { Interval = 5000 };
    private readonly List<(int Group, int Number)> _logicalProcessors = new();
    private readonly bool _isAdmin;

    private CpuSignature? _cpu;
    private string? _cpuGateReason;
    private string _cpuGateMessage = string.Empty;
    private ThrottleStopBaseline? _baseline;
    private bool _samplingBusy;

    private NumericUpDown _numInterval = null!;
    private Button _btnToggleAuto = null!;
    private Label _lblLastUpdate = null!;
    private Label _lblMechanism = null!;
    private Label _banner = null!;

    private readonly Dictionary<string, Label> _idValues = new();

    private Label _lblTargetHex = null!;
    private Label _lblTargetBin = null!;
    private Label _lblTargetDec = null!;
    private Label _lblTjMax = null!;
    private Label _lblOffsetTgl = null!;
    private Label _lblOffsetSdm = null!;
    private Label _lblTau = null!;
    private Label _lblAvg = null!;
    private DataGridView _gridTargetLp = null!;
    private RichTextBox _txtTargetNotes = null!;

    private DataGridView _gridCore = null!;
    private DataGridView _gridEvents = null!;
    private Label _lblPackageSummary = null!;
    private Label _lblProchot = null!;
    private Label _lblThermalTime = null!;

    private NumericUpDown _numRegisterLp = null!;
    private DataGridView _gridRegisters = null!;

    private ComboBox _cmbMechanism = null!;
    private ComboBox _cmbManualMsr = null!;
    private TextBox _txtHexInput = null!;
    private RichTextBox _txtManualResult = null!;

    private DataGridView _gridAudit = null!;
    private Label _lblAuditCount = null!;

    private DataGridView _gridComparison = null!;
    private Label _lblComparisonInfo = null!;

    public MainForm()
    {
        Text = UiTexts.AppTitle;
        Font = new Font("Segoe UI", 9.5F);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 760);
        Size = new Size(1280, 820);

        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            _isAdmin = new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            _isAdmin = false;
        }

        DetectTopology();
        _engine = new DiagnosticEngine(_unavailable, _audit, _logicalProcessors);

        BuildUi();

        _sampleTimer.Tick += async (_, _) => await RunSamplingAsync();
        Shown += async (_, _) =>
        {
            await Task.Run(FillIdentificationTab);
            FillSecurityTab();
        };
    }

    // ------------------------------------------------------------------ topologia / hardware

    /// <summary>
    /// Enumera os processadores logicos. A contagem vem das subchaves de
    /// HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor (uma por processador logico), com
    /// fallback para Environment.ProcessorCount. Para 8 processadores logicos o Windows usa um
    /// unico grupo de processadores (grupos so sao criados acima de 64 LPs) — premissa
    /// documentada na interface. Em ambiente de desenvolvimento nao-Windows, um conjunto
    /// sintetico e criado para permitir revisao da interface (rotulado como tal).
    /// </summary>
    private void DetectTopology()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                for (var i = 0; i < Math.Max(1, Environment.ProcessorCount); i++)
                {
                    _logicalProcessors.Add((0, i));
                }

                _cpuGateMessage =
                    "Ambiente não-Windows (revisão do código): leituras indisponíveis; interface em modo de revisão.";
                return;
            }

            using var baseKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor");
            var lpCount = baseKey?.SubKeyCount ?? Environment.ProcessorCount;
            for (var i = 0; i < lpCount; i++)
            {
                _logicalProcessors.Add((0, i)); // grupo 0: premissa documentada para <= 64 LPs
            }
        }
        catch (Exception ex)
        {
            var count = Math.Max(1, Environment.ProcessorCount);
            for (var i = 0; i < count; i++)
            {
                _logicalProcessors.Add((0, i));
            }

            _cpuGateMessage = $"Topologia não pôde ser lida ({ex.Message}); usando {count} processadores lógicos como premissa.";
        }
    }

    /// <summary>Preenche a aba de identificacao com dados do registro do Windows (somente leitura).</summary>
    private void FillIdentificationTab()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                SetId("cpu", "Não disponível — ambiente não-Windows (revisão do código)");
                SetId("signature", "Não disponível — " + UiTexts.CpuIdNote);
                SetId("fms", "Não disponível");
                SetId("arch", System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString());
                SetId("cores", "Não disponível — ambiente não-Windows");
                SetId("lps", _logicalProcessors.Count.ToString());
                SetId("os", "Não disponível — ambiente não-Windows");
                SetId("admin", _isAdmin ? "Sim" : "Não");
                return;
            }

            using var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var identifier = cpuKey?.GetValue("Identifier") as string ?? string.Empty;
            var brand = cpuKey?.GetValue("ProcessorNameString") as string ?? string.Empty;
            var mhz = cpuKey?.GetValue("~MHz") as int?;

            if (string.IsNullOrWhiteSpace(identifier))
            {
                SetId("cpu", "Não disponível — chave de registro do processador ausente");
            }
            else
            {
                try
                {
                    _cpu = CpuSignature.Parse(identifier, string.IsNullOrWhiteSpace(brand) ? identifier : brand);
                    _cpuGateReason = _cpu.UnsupportedReason;
                    SetId("cpu", _cpu.BrandName);
                    SetId("signature", _cpu.Identifier);
                    SetId("fms", $"Família {_cpu.Family} (0x{_cpu.Family:X}) · Modelo {_cpu.Model} (0x{_cpu.Model:X}) · Stepping {_cpu.Stepping}"
                               + $" — alvo do projeto: Família 6, Modelo 0x8C (Tiger Lake-U i7-1195G7)");
                }
                catch (FormatException fex)
                {
                    _cpuGateReason = fex.Message;
                    SetId("signature", "Não disponível — " + fex.Message);
                }

                if (mhz is not null)
                {
                    SetId("fms", GetId("fms") + $" · ~{mhz} MHz (informe do sistema)");
                }
            }

            SetId("arch", "x86-64 (Intel64) — " + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
            SetId("lps", _logicalProcessors.Count + " processadores lógicos (grupo 0 — premissa documentada para <= 64 LPs)");
            SetId("cores", QueryCoreCount());

            // Sistema operacional: valores diretos do registro (ProductName e legado "Windows 10"
            // mesmo no Windows 11, por isso preferimos EditionID + DisplayVersion + Build).
            using var osKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var edition = osKey?.GetValue("EditionID") as string ?? "N/D";
            var display = osKey?.GetValue("DisplayVersion") as string ?? "N/D";
            var build = osKey?.GetValue("CurrentBuildNumber") as string ?? "N/D";
            var ubr = osKey?.GetValue("UBR") as int?;
            SetId("os", $"Windows ({edition}), versão {display}, build {build}.{(ubr?.ToString() ?? "N/D")} "
                      + $"— {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");

            SetId("admin", _isAdmin
                ? "Sim (o aplicativo exige elevação para o mecanismo de leitura da Fase 2)"
                : "Não");
        }
        catch (Exception ex)
        {
            SetId("cpu", "Não disponível — falha na identificação: " + ex.Message);
        }
    }

    /// <summary>Numero de nucleos fisicos via WMI (Win32_Processor). Falha = Nao disponivel com motivo.</summary>
    private string QueryCoreCount()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return "Não disponível — ambiente não-Windows";
            }

            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    var cores = item["NumberOfCores"]?.ToString();
                    var lps = item["NumberOfLogicalProcessors"]?.ToString();
                    if (cores is not null)
                    {
                        return $"{cores} núcleos físicos / {lps ?? "N/D"} processadores lógicos (Win32_Processor)";
                    }
                }
            }

            return "Não disponível — WMI não retornou dados";
        }
        catch (Exception ex)
        {
            return "Não disponível — falha no WMI: " + ex.Message;
        }
    }

    private string GetId(string key) => _idValues.TryGetValue(key, out var l) ? l.Text : string.Empty;

    private void SetId(string key, string text)
    {
        if (_idValues.TryGetValue(key, out var label))
        {
            label.Text = text;
        }
    }

    // ------------------------------------------------------------------ construcao da UI

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _banner = new Label
        {
            Dock = DockStyle.Fill,
            Text = UiTexts.Phase1Banner,
            BackColor = Color.FromArgb(255, 247, 224),
            ForeColor = Color.FromArgb(80, 60, 0),
            Padding = new Padding(8),
            Height = 56,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        root.Controls.Add(_banner, 0, 0);
        root.Controls.Add(BuildTopStrip(), 0, 1);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildTabIdentification());
        tabs.TabPages.Add(BuildTabTarget());
        tabs.TabPages.Add(BuildTabThermal());
        tabs.TabPages.Add(BuildTabRegisters());
        tabs.TabPages.Add(BuildTabManual());
        tabs.TabPages.Add(BuildTabAudit());
        tabs.TabPages.Add(BuildTabComparison());
        tabs.TabPages.Add(BuildTabSecurity());
        root.Controls.Add(tabs, 0, 2);

        Controls.Add(root);
    }

    private Control BuildTopStrip()
    {
        var strip = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 6, 0, 6),
            Height = 44,
        };

        strip.Controls.Add(new Label { Text = "Intervalo de amostragem (s):", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        _numInterval = new NumericUpDown { Minimum = 1, Maximum = 3600, Value = 5, Width = 70 };
        _numInterval.Name = "intervalo-amostragem";
        strip.Controls.Add(_numInterval);

        _btnToggleAuto = new Button { Text = "Iniciar amostragem automática", Width = 220 };
        _btnToggleAuto.Name = "btn-toggle-auto";
        _btnToggleAuto.Click += OnToggleAuto;
        strip.Controls.Add(_btnToggleAuto);

        var btnRefresh = new Button { Text = "Atualizar agora (leitura manual)", Width = 240 };
        btnRefresh.Name = "btn-atualizar-agora";
        btnRefresh.Click += async (_, _) => await RunSamplingAsync();
        strip.Controls.Add(btnRefresh);

        _lblMechanism = new Label
        {
            Text = "Mecanismo de leitura: " + _engine.Reader.MechanismName,
            AutoSize = true,
            Margin = new Padding(12, 8, 3, 0),
            ForeColor = Color.FromArgb(140, 20, 20),
            Font = new Font(Font, FontStyle.Bold),
        };
        strip.Controls.Add(_lblMechanism);

        _lblLastUpdate = new Label
        {
            Text = "Última amostragem: —",
            AutoSize = true,
            Margin = new Padding(12, 8, 3, 0),
        };
        _lblLastUpdate.Name = "lbl-ultima-amostragem";
        strip.Controls.Add(_lblLastUpdate);

        return strip;
    }

    private static Label ValueLabel() => new()
    {
        AutoSize = true,
        Font = new Font("Consolas", 9.75F, FontStyle.Bold),
        Text = "Não disponível",
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(3, 1, 3, 1),
    };

    private static TableLayoutPanel TwoColumnPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Padding = new Padding(8) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return panel;
    }

    private static void AddRow(TableLayoutPanel panel, string labelText, Control value)
    {
        var row = panel.RowCount;
        panel.RowCount = row + 1;
        panel.Controls.Add(new Label { Text = labelText, AutoSize = true, Margin = new Padding(3, 4, 12, 2), Font = new Font(panel.Font, FontStyle.Bold) }, 0, row);
        panel.Controls.Add(value, 1, row);
    }

    private static DataGridView MakeGrid(params (string Name, string Header, int Width)[] columns)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            AutoGenerateColumns = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
        };
        foreach (var (name, header, width) in columns)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                Width = width > 0 ? width : 0,
                AutoSizeMode = width > 0 ? DataGridViewAutoSizeColumnMode.None : DataGridViewAutoSizeColumnMode.Fill,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            });
        }

        return grid;
    }

    // --------------------------------------------------------------- aba 1: identificacao

    private TabPage BuildTabIdentification()
    {
        var page = new TabPage("Identificação do hardware");
        var panel = TwoColumnPanel();

        var cpuLabel = ValueLabel();
        _idValues["cpu"] = cpuLabel;
        AddRow(panel, "Nome do processador", cpuLabel);

        var signatureLabel = ValueLabel();
        _idValues["signature"] = signatureLabel;
        AddRow(panel, "Assinatura CPUID (via registro do Windows)", signatureLabel);

        var fmsLabel = ValueLabel();
        _idValues["fms"] = fmsLabel;
        AddRow(panel, "Família / Modelo / Stepping", fmsLabel);

        var archLabel = ValueLabel();
        _idValues["arch"] = archLabel;
        AddRow(panel, "Arquitetura", archLabel);

        var coresLabel = ValueLabel();
        _idValues["cores"] = coresLabel;
        AddRow(panel, "Núcleos físicos", coresLabel);

        var lpsLabel = ValueLabel();
        _idValues["lps"] = lpsLabel;
        AddRow(panel, "Processadores lógicos", lpsLabel);

        var osLabel = ValueLabel();
        _idValues["os"] = osLabel;
        AddRow(panel, "Sistema operacional", osLabel);

        var adminLabel = ValueLabel();
        _idValues["admin"] = adminLabel;
        AddRow(panel, "Executando como Administrador", adminLabel);

        AddRow(panel, "Estado do mecanismo de leitura", BuildMechanismPanel());

        var targetLabel = ValueLabel();
        _idValues["target"] = targetLabel;
        AddRow(panel, "Verificação do hardware alvo", targetLabel);

        page.Controls.Add(panel);

        // Notas metodologicas
        var notes = new RichTextBox
        {
            Dock = DockStyle.Bottom,
            Height = 150,
            ReadOnly = true,
            BackColor = Color.FromArgb(248, 248, 252),
            Font = new Font("Segoe UI", 9F),
        };
        notes.AppendText("Notas metodológicas:" + Environment.NewLine);
        notes.AppendText("• " + UiTexts.CpuIdNote + Environment.NewLine);
        notes.AppendText("• Leituras em processadores não alvo são RECUSADAS (requisito 9.1)." + Environment.NewLine);
        page.Controls.Add(notes);

        return page;
    }

    private Control BuildMechanismPanel()
    {
        var panel = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var lbl = new Label
        {
            Text = _engine.Reader.MechanismName + " — " + _engine.Reader.StatusDescription,
            AutoSize = true,
            MaximumSize = new Size(900, 0),
            Margin = new Padding(3, 1, 3, 1),
        };
        lbl.Name = "lbl-mecanismo-estado";
        panel.Controls.Add(lbl);

        return panel;
    }

    // --------------------------------------------------------------- aba 2: temperatura-alvo

    private TabPage BuildTabTarget()
    {
        var page = new TabPage("Temperatura-alvo (0x1A2)");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var panel = TwoColumnPanel();
        _lblTargetHex = ValueLabel();
        _lblTargetBin = ValueLabel();
        _lblTargetDec = ValueLabel();
        _lblTjMax = ValueLabel();
        _lblOffsetTgl = ValueLabel();
        _lblOffsetSdm = ValueLabel();
        _lblTau = ValueLabel();
        _lblAvg = ValueLabel();
        AddRow(panel, "Valor bruto (hexadecimal, 64 bits)", _lblTargetHex);
        AddRow(panel, "Valor bruto (binário, grupos de 8)", _lblTargetBin);
        AddRow(panel, "Valor bruto (decimal)", _lblTargetDec);
        AddRow(panel, "TjMAX (bits [23:16])", _lblTjMax);
        AddRow(panel, "TCC Activation Offset (bits [29:24] — datasheet TGL)", _lblOffsetTgl);
        AddRow(panel, "TCC Activation Offset (bits [27:24] — SDM, conferência)", _lblOffsetSdm);
        AddRow(panel, "Tau bruto (bits [6:0])", _lblTau);
        AddRow(panel, "Temperatura-alvo média (TjMAX − offset)", _lblAvg);
        layout.Controls.Add(panel, 0, 0);

        _lblTargetDec.Name = "target-valor-dec";

        _gridTargetLp = MakeGrid(
            ("lp", "Processador lógico", 130),
            ("hex", "Valor bruto (hex)", 200),
            ("tjmax", "TjMAX (°C)", 100),
            ("offset", "Offset [29:24]", 110),
            ("tau", "Tau [6:0]", 90));
        _gridTargetLp.Name = "grid-target-lp";
        layout.Controls.Add(_gridTargetLp, 0, 2);

        _txtTargetNotes = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Height = 170,
            ReadOnly = true,
            BackColor = Color.FromArgb(248, 248, 252),
            Font = new Font("Segoe UI", 9F),
        };
        _txtTargetNotes.Name = "target-notas";
        layout.Controls.Add(_txtTargetNotes, 0, 3);

        page.Controls.Add(layout);
        return page;
    }

    // --------------------------------------------------------------- aba 3: status termico

    private TabPage BuildTabThermal()
    {
        var page = new TabPage("Status térmico (0x19C / 0x1B1)");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _lblThermalTime = new Label { Text = "Última leitura: —", AutoSize = true };
        layout.Controls.Add(_lblThermalTime, 0, 0);

        _lblPackageSummary = new Label
        {
            Text = "Pacote (0x1B1): Não disponível — nenhuma amostragem realizada.",
            AutoSize = true,
            MaximumSize = new Size(1100, 0),
        };
        _lblPackageSummary.Name = "lbl-pacote-resumo";
        layout.Controls.Add(_lblPackageSummary, 0, 1);

        _gridCore = MakeGrid(
            ("lp", "Processador lógico", 120),
            ("temp", "Temperatura (°C)", 120),
            ("readout", "Digital Readout [22:16]", 150),
            ("status", "Thermal Status (corrente)", 170),
            ("log", "Thermal Status Log (histórico)", 190),
            ("prochot", "PROCHOT Event (corrente/externo)", 220),
            ("prochotlog", "PROCHOT Log (histórico)", 170),
            ("crit", "Critical Temp (corrente/log)", 180),
            ("power", "Power Limit (corrente/log)", 170));
        _gridCore.Name = "grid-nucleos";
        layout.Controls.Add(_gridCore, 0, 2);

        _gridEvents = MakeGrid(
            ("quando", "Detectado em (UTC)", 150),
            ("msr", "MSR", 80),
            ("escopo", "Escopo", 120),
            ("indicador", "Indicador", 230),
            ("tipo", "Tipo", 90),
            ("de", "De", 130),
            ("para", "Para", 130),
            ("nota", "Nota de honestidade temporal", 400));
        _gridEvents.Name = "grid-eventos";
        layout.Controls.Add(_gridEvents, 0, 3);

        var prochotPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };
        prochotPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _lblProchot = new Label
        {
            Text = "PROCHOT: Não disponível — nenhuma amostragem realizada.",
            AutoSize = true,
            MaximumSize = new Size(1100, 0),
        };
        _lblProchot.Name = "lbl-prochot-avaliacao";
        prochotPanel.Controls.Add(_lblProchot);
        layout.Controls.Add(prochotPanel, 0, 4);

        page.Controls.Add(layout);
        return page;
    }

    // --------------------------------------------------------------- aba 4: registradores

    private TabPage BuildTabRegisters()
    {
        var page = new TabPage("Registradores (valores brutos)");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var selector = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Height = 40 };
        selector.Controls.Add(new Label { Text = "Processador lógico:", AutoSize = true, Margin = new Padding(3, 6, 3, 0) });
        _numRegisterLp = new NumericUpDown { Minimum = 0, Maximum = Math.Max(0, _logicalProcessors.Count - 1), Value = 0, Width = 60 };
        _numRegisterLp.Name = "selector-lp-registradores";
        _numRegisterLp.ValueChanged += (_, _) => RefreshRegisterGrid();
        selector.Controls.Add(_numRegisterLp);
        layout.Controls.Add(selector, 0, 0);

        _gridRegisters = MakeGrid(
            ("msr", "Registrador", 340),
            ("endereco", "Endereço", 90),
            ("hex", "Hexadecimal", 200),
            ("bin", "Binário (grupos de 8)", 520),
            ("dec", "Decimal", 150));
        _gridRegisters.Name = "grid-registradores";
        layout.Controls.Add(_gridRegisters, 0, 1);

        var actions = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Height = 44 };
        var btnCopy = new Button { Text = "Copiar valores para a área de transferência", Width = 300 };
        btnCopy.Name = "btn-copiar-registradores";
        btnCopy.Click += OnCopyRegisters;
        actions.Controls.Add(btnCopy);
        actions.Controls.Add(new Label
        {
            Text = "Escopos documentados: 0x19C por processador lógico · 0x1B1 por pacote · 0x1A2 por processador lógico (SDM Vol. 4).",
            AutoSize = true,
            Margin = new Padding(12, 8, 3, 0),
        });
        layout.Controls.Add(actions, 0, 2);

        page.Controls.Add(layout);
        return page;
    }

    private void OnCopyRegisters(object? sender, EventArgs e)
    {
        try
        {
            var sb = new StringBuilder();
            foreach (DataGridViewRow row in _gridRegisters.Rows)
            {
                sb.AppendLine(string.Join(" | ", _gridRegisters.Columns
                    .Cast<DataGridViewColumn>()
                    .Select(c => $"{c.HeaderText}: {row.Cells[c.Index].Value}")));
            }

            Clipboard.SetText(sb.ToString());
            MessageBox.Show(this, "Valores copiados.", "Copiar", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não disponível — falha ao copiar: " + ex.Message, "Copiar",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // --------------------------------------------------------------- aba 5: teste manual

    private TabPage BuildTabManual()
    {
        var page = new TabPage("Teste manual (SIMULADO)");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var warn = new Label
        {
            Text = "ESTA ABA DECODIFICA VALORES FORNECIDOS MANUALMENTE. NÃO REALIZA LEITURA DO HARDWARE. "
                 + "VALORES MANUAIS SÃO SEMPRE ROTULADOS COMO SIMULADOS.",
            BackColor = Color.FromArgb(255, 247, 224),
            ForeColor = Color.FromArgb(80, 60, 0),
            Padding = new Padding(8),
            Height = 48,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        layout.Controls.Add(warn, 0, 0);

        var form = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, Dock = DockStyle.Fill };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        form.Controls.Add(new Label { Text = "Mecanismo:", AutoSize = true, Margin = new Padding(3, 6, 3, 0) }, 0, 0);
        _cmbMechanism = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
        _cmbMechanism.Items.Add(_unavailable.MechanismName);
        _cmbMechanism.Items.Add(ManualMsrReader.MechanismLabel);
        _cmbMechanism.SelectedIndex = 0;
        _cmbMechanism.Name = "combo-mecanismo";
        _cmbMechanism.SelectedIndexChanged += OnMechanismChanged;
        form.Controls.Add(_cmbMechanism, 1, 0);

        form.Controls.Add(new Label { Text = "Registrador:", AutoSize = true, Margin = new Padding(18, 6, 3, 0) }, 2, 0);
        _cmbManualMsr = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
        foreach (var address in MsrAddress.All)
        {
            _cmbManualMsr.Items.Add($"{MsrAddress.Name(address)} — 0x{address:X}");
        }

        _cmbManualMsr.SelectedIndex = 0;
        _cmbManualMsr.Name = "combo-registrador-manual";
        form.Controls.Add(_cmbManualMsr, 3, 0);

        form.Controls.Add(new Label { Text = "Valor bruto (hex, 64 bits):", AutoSize = true, Margin = new Padding(3, 6, 3, 0) }, 0, 1);
        _txtHexInput = new TextBox { Width = 260, Font = new Font("Consolas", 10F), Text = "" };
        _txtHexInput.Name = "input-hex-manual";
        form.Controls.Add(_txtHexInput, 1, 1);
        form.SetColumnSpan(_txtHexInput, 3);

        var actions = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Dock = DockStyle.Fill };
        var btnDecode = new Button { Text = "Decodificar valor bruto", Width = 220 };
        btnDecode.Name = "btn-decodificar-manual";
        btnDecode.Click += OnManualDecode;
        actions.Controls.Add(btnDecode);

        var btnApply = new Button { Text = "Aplicar como valor manual (todos os LPs)", Width = 300 };
        btnApply.Name = "btn-aplicar-manual";
        btnApply.Click += OnManualApply;
        actions.Controls.Add(btnApply);
        layout.Controls.Add(actions, 0, 2);

        _txtManualResult = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(248, 248, 252),
        };
        _txtManualResult.Name = "resultado-manual";
        layout.Controls.Add(_txtManualResult, 0, 3);

        page.Controls.Add(layout);
        return page;
    }

    private void OnMechanismChanged(object? sender, EventArgs e)
    {
        if (_cmbMechanism.SelectedIndex == 1)
        {
            _engine.SetReader(_manual);
            _banner.Text = UiTexts.SimulatedBanner;
            _banner.BackColor = Color.FromArgb(255, 224, 224);
        }
        else
        {
            _engine.SetReader(_unavailable);
            _banner.Text = UiTexts.Phase1Banner;
            _banner.BackColor = Color.FromArgb(255, 247, 224);
        }

        _lblMechanism.Text = "Mecanismo de leitura: " + _engine.Reader.MechanismName;
        RefreshMechanismState();
    }

    private void RefreshMechanismState()
    {
        var mechanismState = Controls.Find("lbl-mecanismo-estado", true).FirstOrDefault() as Label;
        if (mechanismState is not null)
        {
            mechanismState.Text = _engine.Reader.MechanismName + " — " + _engine.Reader.StatusDescription
                                + (_engine.Reader.IsSimulated ? " [SIMULADO]" : string.Empty);
        }

        if (_idValues.TryGetValue("target", out var target))
        {
            target.Text = _cpu is { UnsupportedReason: null }
                ? "Confirmado: Tiger Lake-U i7-1195G7 (Família 6, Modelo 0x8C)"
                : _cpuGateReason is not null ? "RECUSADO — " + _cpuGateReason : "Pendente — aguardando identificação";
            target.ForeColor = _cpu is { UnsupportedReason: null } ? Color.FromArgb(0, 100, 0) : Color.FromArgb(140, 20, 20);
        }
    }

    private static bool TryParseHex(string? text, out ulong value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var clean = text.Trim();
        if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean[2..];
        }

        clean = clean.Replace(" ", string.Empty).Replace("_", string.Empty);
        return clean.Length is >= 1 and <= 16
            && ulong.TryParse(clean, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private void OnManualDecode(object? sender, EventArgs e)
    {
        if (!TryParseHex(_txtHexInput.Text, out var value))
        {
            MessageBox.Show(this, "Valor hexadecimal inválido. Informe de 1 a 16 dígitos hexadecimais (opcional prefixo 0x).",
                "Teste manual", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("VALOR FORNECIDO MANUALMENTE — SIMULADO (não é leitura de hardware)");
        sb.AppendLine();
        AppendRegisterDecoding(sb, value);

        _txtManualResult.Text = sb.ToString();
    }

    private void AppendRegisterDecoding(StringBuilder sb, ulong value)
    {
        var address = MsrAddress.All[Math.Max(0, _cmbManualMsr.SelectedIndex)];

        sb.AppendLine($"{MsrAddress.Name(address)} (0x{address:X})");
        sb.AppendLine("Hex: " + Bits.Hex16(value));
        sb.AppendLine("Bin: " + Bits.Bin64(value));
        sb.AppendLine("Dec: " + value);
        sb.AppendLine("Escopo: " + MsrAddress.Scope(address));
        sb.AppendLine();
        sb.AppendLine("Campos decodificados (máscara · valor bruto · significado · citação):");

        IReadOnlyList<DecodedField> fields = address switch
        {
            MsrAddress.TemperatureTarget => TemperatureTargetDecoding.Decode(value).Fields,
            MsrAddress.ThermStatus => ThermStatusDecoding.Decode(value, tjMaxC: null).Fields,
            MsrAddress.PackageThermStatus => PackageThermStatusDecoding.Decode(value, tjMaxC: null).Fields,
            _ => Array.Empty<DecodedField>(),
        };

        foreach (var field in fields)
        {
            sb.AppendLine($"  {field.Bits,-9} {field.Name} = {field.RawValue} (0x{field.RawValue:X})  [{field.Kind}]");
            sb.AppendLine($"      máscara: 0x{field.Mask:X16}; acesso: {field.Access}");
            sb.AppendLine($"      {field.Meaning}");
            sb.AppendLine($"      Fonte: {field.Citation}");
            if (field.Note is not null)
            {
                sb.AppendLine($"      Nota: {field.Note}");
            }

            sb.AppendLine();
        }

        if (address == MsrAddress.TemperatureTarget)
        {
            var decoded = TemperatureTargetDecoding.Decode(value);
            sb.AppendLine($"TjMAX: {decoded.TjMaxC} °C");
            sb.AppendLine($"TCC Activation Offset ([29:24], datasheet TGL): {decoded.OffsetTgl} °C");
            sb.AppendLine($"TCC Activation Offset ([27:24], SDM genérico): {decoded.OffsetSdm} °C"
                        + (decoded.OffsetDivergent ? "  — DIVERGÊNCIA ENTRE MÁSCARAS" : string.Empty));
            sb.AppendLine($"Tau (bruto, sem conversão): {decoded.TauRaw}");
            sb.AppendLine($"Temperatura-alvo média: {decoded.AverageTargetC} °C");
            sb.AppendLine();
            sb.AppendLine("Limitações: " + UiTexts.TauLimitation);
        }
    }

    private void OnManualApply(object? sender, EventArgs e)
    {
        if (!TryParseHex(_txtHexInput.Text, out var value))
        {
            MessageBox.Show(this, "Valor hexadecimal inválido. Informe de 1 a 16 dígitos hexadecimais (opcional prefixo 0x).",
                "Teste manual", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_cmbMechanism.SelectedIndex != 1)
        {
            _cmbMechanism.SelectedIndex = 1; // dispara OnMechanismChanged
        }

        var address = MsrAddress.All[Math.Max(0, _cmbManualMsr.SelectedIndex)];
        _manual.SetValueForAll(address, _logicalProcessors.Select(p => p.Number), value);
        _ = Task.Run(async () =>
        {
            await RunSamplingAsync();
        });
    }

    // --------------------------------------------------------------- aba 6: auditoria

    private TabPage BuildTabAudit()
    {
        var page = new TabPage("Auditoria das leituras");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var info = new Label
        {
            Text = "Registro por leitura: processador lógico · endereço do MSR · valor bruto · data/hora · resultado · erro. "
                 + "O log é apenas para acréscimo (nunca limpo). Leituras simuladas são marcadas.",
            AutoSize = true,
            MaximumSize = new Size(1150, 0),
        };
        layout.Controls.Add(info, 0, 0);

        _gridAudit = MakeGrid(
            ("quando", "Data/hora (UTC)", 150),
            ("grupo", "Grupo", 60),
            ("lp", "Proc. lógico", 90),
            ("msr", "MSR", 90),
            ("nome", "Registrador", 300),
            ("mecanismo", "Mecanismo", 220),
            ("status", "Resultado", 90),
            ("hex", "Valor bruto (hex)", 190),
            ("erro", "Erro", 330),
            ("sim", "Simulado", 80));
        _gridAudit.Name = "grid-auditoria";
        layout.Controls.Add(_gridAudit, 0, 1);

        var actions = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Height = 46 };
        _lblAuditCount = new Label { Text = "0 leituras registradas", AutoSize = true, Margin = new Padding(3, 8, 12, 0) };
        _lblAuditCount.Name = "lbl-auditoria-contagem";
        actions.Controls.Add(_lblAuditCount);

        var btnCsv = new Button { Text = "Exportar CSV", Width = 140 };
        btnCsv.Name = "btn-exportar-csv";
        btnCsv.Click += (_, _) => ExportAudit(ExportFormat.Csv);
        actions.Controls.Add(btnCsv);

        var btnJson = new Button { Text = "Exportar JSON", Width = 150 };
        btnJson.Name = "btn-exportar-json";
        btnJson.Click += (_, _) => ExportAudit(ExportFormat.Json);
        actions.Controls.Add(btnJson);

        var btnNdjson = new Button { Text = "Exportar NDJSON", Width = 160 };
        btnNdjson.Name = "btn-exportar-ndjson";
        btnNdjson.Click += (_, _) => ExportAudit(ExportFormat.Ndjson);
        actions.Controls.Add(btnNdjson);

        layout.Controls.Add(actions, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private enum ExportFormat
    {
        Csv,
        Json,
        Ndjson,
    }

    private void ExportAudit(ExportFormat format)
    {
        try
        {
            var records = _audit.Snapshot();
            if (records.Count == 0)
            {
                MessageBox.Show(this, "Nenhuma leitura registrada ainda.", "Exportar", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Filter = format switch
                {
                    ExportFormat.Csv => "CSV (*.csv)|*.csv",
                    ExportFormat.Json => "JSON (*.json)|*.json",
                    _ => "NDJSON (*.ndjson)|*.ndjson",
                },
                FileName = format switch
                {
                    ExportFormat.Csv => "auditoria-msr.csv",
                    ExportFormat.Json => "diagnostico-msr.json",
                    _ => "auditoria-msr.ndjson",
                },
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var content = format switch
            {
                ExportFormat.Csv => ExportService.AuditCsv(records),
                ExportFormat.Json => ExportService.DiagnosticsJson(records, _engine.TransitionLog,
                    _engine.Reader.MechanismName, _engine.Reader.IsSimulated, _engine.LastSample?.Notes ?? Array.Empty<string>()),
                _ => ExportService.Ndjson(records),
            };

            File.WriteAllText(dialog.FileName, content, Encoding.UTF8);
            MessageBox.Show(this, "Exportação concluída: " + dialog.FileName, "Exportar",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não disponível — falha na exportação: " + ex.Message, "Exportar",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // --------------------------------------------------------------- aba 7: comparacao

    private TabPage BuildTabComparison()
    {
        var page = new TabPage("Comparação ThrottleStop (antes/depois)");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var instructions = new Label
        {
            Text = "Procedimento (requisito 14.1-B): 1) feche o ThrottleStop; 2) reinicie o Windows; 3) aqui, clique em "
                 + "\"Registrar linha de base (ANTES)\"; 4) inicie o ThrottleStop sem alterar configurações; 5) aqui, clique em "
                 + "\"Comparar agora (DEPOIS)\". O aplicativo nunca escreve nos registradores nem altera configurações do "
                 + "ThrottleStop. A comparação registra diferenças, mas NÃO prova causalidade.",
            AutoSize = true,
            MaximumSize = new Size(1150, 0),
        };
        layout.Controls.Add(instructions, 0, 0);

        _gridComparison = MakeGrid(
            ("msr", "MSR", 90),
            ("nome", "Registrador", 330),
            ("lp", "Proc. lógico (-1 = pacote)", 160),
            ("antes", "ANTES (hex)", 200),
            ("depois", "DEPOIS (hex)", 200),
            ("mudou", "Mudou", 80));
        _gridComparison.Name = "grid-comparacao";
        layout.Controls.Add(_gridComparison, 0, 1);

        _lblComparisonInfo = new Label { Text = "Linha de base: —", AutoSize = true };
        _lblComparisonInfo.Name = "lbl-comparacao-info";
        layout.Controls.Add(_lblComparisonInfo, 0, 2);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Height = 46 };
        var btnBaseline = new Button { Text = "Registrar linha de base (ANTES)", Width = 260 };
        btnBaseline.Name = "btn-baseline-antes";
        btnBaseline.Click += async (_, _) =>
        {
            await RunSamplingAsync();
            if (_engine.LastSample is not null)
            {
                _baseline = ThrottleStopComparison.FromSample(_engine.LastSample, "ANTES");
                _lblComparisonInfo.Text = $"Linha de base registrada em {_baseline.TimestampUtc:HH:mm:ss} UTC "
                                        + (_baseline.Simulated ? " (MODO SIMULADO)" : string.Empty)
                                        + ". Inicie o ThrottleStop e clique em \"Comparar agora (DEPOIS)\".";
            }
        };
        buttons.Controls.Add(btnBaseline);

        var btnCompare = new Button { Text = "Comparar agora (DEPOIS)", Width = 230 };
        btnCompare.Name = "btn-comparar-depois";
        btnCompare.Click += async (_, _) =>
        {
            if (_baseline is null)
            {
                MessageBox.Show(this, "Registre primeiro a linha de base (ANTES).", "Comparação",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            await RunSamplingAsync();
            if (_engine.LastSample is null)
            {
                return;
            }

            var after = ThrottleStopComparison.FromSample(_engine.LastSample, "DEPOIS");
            var report = ThrottleStopComparison.Compare(_baseline, after);
            FillComparisonGrid(report);
            _lblComparisonInfo.Text = $"Comparação concluída em {report.ComparedAtUtc:HH:mm:ss} UTC "
                                    + (report.Simulated ? " (MODO SIMULADO — valores não são leituras reais)" : string.Empty);
        };
        buttons.Controls.Add(btnCompare);
        actions.Controls.Add(buttons);

        var disclaimer = new Label
        {
            Text = UiTexts.ComparisonDisclaimer,
            AutoSize = true,
            MaximumSize = new Size(1150, 0),
            ForeColor = Color.FromArgb(90, 60, 0),
        };
        actions.Controls.Add(disclaimer);
        layout.Controls.Add(actions, 0, 3);

        page.Controls.Add(layout);
        return page;
    }

    private void FillComparisonGrid(ComparisonReport report)
    {
        _gridComparison.Rows.Clear();
        foreach (var entry in report.Entries)
        {
            _gridComparison.Rows.Add(
                $"0x{entry.MsrAddress:X}",
                entry.MsrName,
                entry.ProcessorNumber < 0 ? "Pacote" : entry.ProcessorNumber.ToString(),
                entry.BeforeHex,
                entry.AfterHex,
                entry.Changed ? "SIM" : "não");
        }
    }

    // --------------------------------------------------------------- aba 8: seguranca

    private TabPage BuildTabSecurity()
    {
        var page = new TabPage("Segurança");
        var text = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Segoe UI", 9.5F),
            BackColor = Color.FromArgb(248, 248, 252),
        };
        text.Name = "txt-seguranca";
        page.Controls.Add(text);
        return page;
    }

    private void FillSecurityTab()
    {
        var text = Controls.Find("txt-seguranca", true).FirstOrDefault() as RichTextBox;
        if (text is null)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("POLÍTICA DE SEGURANÇA — SOMENTE LEITURA (requisito 8 do escopo)");
        sb.AppendLine();
        sb.AppendLine("Operações permitidas: leitura (RDMSR conceitual) dos MSRs 0x1A2, 0x19C e 0x1B1.");
        sb.AppendLine("Operações proibidas e NÃO IMPLEMENTADAS: WRMSR; escrita em MSR/MMIO/PCI Config/memória física;");
        sb.AppendLine("alteração de PL1/PL2, PP0/PP1, PROCHOT, TCC Offset, tensões ou limites de potência;");
        sb.AppendLine("alteração de configurações do Windows (bcdedit/Secure Boot/VBS/HVCI/Integridade da Memória/TESTSIGNING).");
        sb.AppendLine();
        sb.AppendLine("Estado atual (Fase 1): nenhum driver instalado, nenhuma alteração no sistema realizada.");
        sb.AppendLine("A Fase 2 (driver somente-leitura) depende de revisão técnica e autorização explícita.");
        sb.AppendLine();
        sb.AppendLine("Auto-verificações estruturais:");
        foreach (var (assertion, evidence) in ReadPolicy.SelfCheck())
        {
            sb.AppendLine("• " + assertion);
            sb.AppendLine("    " + evidence);
        }

        text.Text = sb.ToString();
    }

    // ------------------------------------------------------------------ amostragem

    private void OnToggleAuto(object? sender, EventArgs e)
    {
        if (_sampleTimer.Enabled)
        {
            _sampleTimer.Stop();
            _btnToggleAuto.Text = "Iniciar amostragem automática";
            return;
        }

        _sampleTimer.Interval = (int)_numInterval.Value * 1000;
        _sampleTimer.Start();
        _btnToggleAuto.Text = "Pausar amostragem";
        _ = RunSamplingAsync();
    }

    private async Task RunSamplingAsync()
    {
        if (_samplingBusy)
        {
            return;
        }

        if (_cpuGateReason is not null)
        {
            _lblLastUpdate.Text = "Leituras recusadas: " + _cpuGateReason;
            MessageBox.Show(this,
                "Leituras recusadas — hardware não compatível com o alvo do projeto." + Environment.NewLine
                + Environment.NewLine + _cpuGateReason,
                "Identificação do hardware", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _samplingBusy = true;
        try
        {
            var snapshot = await Task.Run(_engine.CaptureSample);
            UpdateFromSample(snapshot);
        }
        catch (Exception ex)
        {
            _lblLastUpdate.Text = "Falha na amostragem: " + ex.Message;
        }
        finally
        {
            _samplingBusy = false;
        }
    }

    private void UpdateFromSample(SampleSnapshot snapshot)
    {
        var local = snapshot.TimestampUtc.ToLocalTime();
        _lblLastUpdate.Text = $"Última amostragem: {local:HH:mm:ss} ({snapshot.TimestampUtc:HH:mm:ss} UTC) — "
                            + $"mecanismo: {snapshot.MechanismName}" + (snapshot.Simulated ? " [SIMULADO]" : string.Empty);

        UpdateTargetTab(snapshot);
        UpdateThermalTab(snapshot);
        RefreshRegisterGrid();
        RefreshAuditGrid();
    }

    private static string Nda(string? reason) => reason is null
        ? "Não disponível"
        : "Não disponível — " + reason;

    private void UpdateTargetTab(SampleSnapshot snapshot)
    {
        var decoded = snapshot.TemperatureTarget;
        if (decoded is null)
        {
            var reason = snapshot.TemperatureTargetReadings.FirstOrDefault()?.ErrorMessage
                         ?? "nenhuma amostragem realizada";
            _lblTargetHex.Text = Nda(reason);
            _lblTargetBin.Text = Nda(reason);
            _lblTargetDec.Text = Nda(reason);
            _lblTjMax.Text = Nda(reason);
            _lblOffsetTgl.Text = Nda(reason);
            _lblOffsetSdm.Text = Nda(reason);
            _lblTau.Text = Nda(reason);
            _lblAvg.Text = Nda(reason);
            _gridTargetLp.Rows.Clear();
            _txtTargetNotes.Text = UiTexts.OffsetNote + Environment.NewLine + UiTexts.TauLimitation;
            return;
        }

        _lblTargetHex.Text = Bits.Hex16(decoded.Raw);
        _lblTargetBin.Text = Bits.Bin64(decoded.Raw);
        _lblTargetDec.Text = decoded.Raw.ToString();
        _lblTjMax.Text = $"{decoded.TjMaxC} °C (lido de [23:16]; não presumido)";
        _lblOffsetTgl.Text = $"{decoded.OffsetTgl} °C";
        _lblOffsetSdm.Text = $"{decoded.OffsetSdm} °C" + (decoded.OffsetDivergent ? "  — DIVERGÊNCIA ENTRE AS MÁSCARAS" : string.Empty);
        _lblTau.Text = $"{decoded.TauRaw} (valor bruto — sem conversão documentada para segundos)";
        _lblAvg.Text = $"{decoded.AverageTargetC} °C";

        _gridTargetLp.Rows.Clear();
        for (var i = 0; i < snapshot.TemperatureTargetReadings.Count; i++)
        {
            var reading = snapshot.TemperatureTargetReadings[i];
            var lp = i < snapshot.CoreReadings.Count ? snapshot.CoreReadings[i].ProcessorNumber : i;
            if (reading.Success)
            {
                var perLp = TemperatureTargetDecoding.Decode(reading.Value);
                _gridTargetLp.Rows.Add(lp.ToString(), reading.ValueHex, perLp.TjMaxC.ToString(), perLp.OffsetTgl.ToString(), perLp.TauRaw.ToString());
            }
            else
            {
                _gridTargetLp.Rows.Add(lp.ToString(), "Não disponível", "—", "—", "—");
            }
        }

        var notes = new StringBuilder();
        notes.AppendLine(UiTexts.OffsetNote);
        notes.AppendLine();
        notes.AppendLine(UiTexts.TauLimitation);
        notes.AppendLine();
        notes.AppendLine("O TCC Activation Offset não substitui o TjMAX: o controle térmico também atua no limite crítico "
                       + "documentado pelo fabricante. Elevar o offset não resolve a causa do aquecimento.");
        foreach (var note in snapshot.Notes)
        {
            notes.AppendLine();
            notes.AppendLine("• " + note);
        }

        _txtTargetNotes.Text = notes.ToString();
    }

    private void UpdateThermalTab(SampleSnapshot snapshot)
    {
        _lblThermalTime.Text = $"Última leitura: {snapshot.TimestampUtc.ToLocalTime():HH:mm:ss} ({snapshot.TimestampUtc:HH:mm:ss} UTC)"
                             + (snapshot.Simulated ? " [SIMULADO]" : string.Empty);

        _gridCore.Rows.Clear();
        foreach (var reading in snapshot.CoreReadings)
        {
            var d = reading.Decoded;
            _gridCore.Rows.Add(
                reading.ProcessorNumber.ToString(),
                d?.TemperatureC is { } temp ? temp + " °C" : Nda(d is null ? reading.Result.ErrorMessage ?? "leitura inválida" : "leitura inválida (Reading Valid = 0)"),
                d is null ? "—" : d.DigitalReadout.ToString(),
                d is null ? "—" : (d.ThermalStatus ? "ATIVO" : "inativo"),
                d is null ? "—" : (d.ThermalStatusLog ? "REGISTRADO (histórico)" : "não registrado"),
                d is null ? "—" : (d.ProchotEvent ? "ASSERTIDO POR COMPONENTE EXTERNO" : "inativo"),
                d is null ? "—" : (d.ProchotLog ? "REGISTRADO (histórico)" : "não registrado"),
                d is null ? "—" : $"{(d.CriticalTempStatus ? "ATIVO" : "inativo")}/{(d.CriticalTempLog ? "registrado" : "não")}",
                d is null ? "—" : $"{(d.PowerLimitStatus ? "ATIVO" : "inativo")}/{(d.PowerLimitLog ? "registrado" : "não")}");
        }

        var pkg = snapshot.Package;
        if (pkg is null)
        {
            var reason = snapshot.PackageReadings.FirstOrDefault()?.Result.ErrorMessage ?? "nenhuma amostragem realizada";
            _lblPackageSummary.Text = "Pacote (0x1B1): " + Nda(reason);
            _lblProchot.Text = "PROCHOT: " + Nda(reason);
        }
        else
        {
            _lblPackageSummary.Text =
                "Pacote (0x1B1): thermal status " + (pkg.ThermalStatus ? "ATIVO" : "inativo")
                + " · log " + (pkg.ThermalStatusLog ? "REGISTRADO (histórico)" : "não registrado")
                + " · PROCHOT externo " + (pkg.ProchotEvent ? "ATIVO" : "inativo")
                + " · PROCHOT log " + (pkg.ProchotLog ? "REGISTRADO" : "não registrado")
                + " · temperatura " + (pkg.TemperatureC is { } pt ? pt + " °C" : "Não disponível")
                + " · leitura válida " + (snapshot.PackageReadings.All(r => r.Result.Success) ? "sim" : "parcial");

            var firstCore = snapshot.CoreReadings.FirstOrDefault(r => r.Decoded is not null)?.Decoded
                            ?? ThermStatusDecoding.Decode(0, tjMaxC: null);
            var assessment = ProchotAssessment.Assess(firstCore, pkg);
            _lblProchot.Text = $"PROCHOT — {assessment.Classification}. {assessment.Detail} {assessment.Caveat}";
        }

        _gridEvents.Rows.Clear();
        foreach (var transition in _engine.TransitionLog.AsEnumerable().Reverse())
        {
            _gridEvents.Rows.Add(
                transition.DetectedAtUtc.ToString("HH:mm:ss"),
                $"0x{transition.MsrAddress:X}",
                transition.Scope,
                transition.Indicator,
                transition.Kind,
                transition.From,
                transition.To,
                transition.HonestTimestampNote);
        }
    }

    private void RefreshRegisterGrid()
    {
        var snapshot = _engine.LastSample;
        if (snapshot is null)
        {
            if (_gridRegisters.Rows.Count == 0)
            {
                foreach (var address in MsrAddress.All)
                {
                    _gridRegisters.Rows.Add(MsrAddress.Name(address), $"0x{address:X}", "Não disponível", "—", "—");
                }
            }

            return;
        }

        var lp = (int)_numRegisterLp.Value;
        var core = snapshot.CoreReadings.FirstOrDefault(r => r.ProcessorNumber == lp);
        var target = snapshot.TemperatureTargetReadings.Count > lp
            ? snapshot.TemperatureTargetReadings[lp]
            : null;
        var package = snapshot.PackageReadings.FirstOrDefault(r => r.Result.Success)?.Result;

        _gridRegisters.Rows.Clear();

        void AddRow(int address, MsrReadResult? result)
        {
            if (result is { Success: true })
            {
                _gridRegisters.Rows.Add(
                    MsrAddress.Name(address),
                    $"0x{address:X}",
                    result.ValueHex,
                    Bits.Bin64(result.Value),
                    result.Value.ToString());
            }
            else
            {
                _gridRegisters.Rows.Add(
                    MsrAddress.Name(address),
                    $"0x{address:X}",
                    Nda(result?.ErrorMessage ?? "nenhuma amostragem"),
                    "—",
                    "—");
            }
        }

        AddRow(MsrAddress.TemperatureTarget, target);
        AddRow(MsrAddress.ThermStatus, core?.Result);
        AddRow(MsrAddress.PackageThermStatus, package); // escopo: pacote (mesmo valor em qualquer LP)
    }

    private void RefreshAuditGrid()
    {
        var records = _audit.Snapshot();
        _gridAudit.Rows.Clear();
        foreach (var record in records)
        {
            _gridAudit.Rows.Add(
                record.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                record.ProcessorGroup.ToString(),
                record.ProcessorNumber.ToString(),
                $"0x{record.MsrAddress:X}",
                record.MsrName,
                record.Mechanism,
                record.Status,
                record.RawHex ?? "—",
                record.Error ?? "—",
                record.Simulated ? "SIM" : "não");
        }

        _lblAuditCount.Text = records.Count + " leituras registradas (log apenas para acréscimo)";
    }
}
