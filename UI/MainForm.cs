using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using HiddenStock.Core;

namespace HiddenStock.UI;

public sealed class MainForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private readonly Panel _content;
    private readonly Label _statusLabel;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;

    private readonly List<string> _roots = new();
    private readonly List<string> _files = new();

    private ListBox? _fileList;
    private Label? _selectionInfo;
    private DateTimePicker? _datePicker;
    private CheckBox? _deleteOriginals;
    private ModernButton? _sealButton;
    private ProgressBar? _progress;
    private Label? _progressLabel;
    private Label? _listPlaceholder;

    private Label? _bigNumber;
    private Label? _bigUnit;
    private Label? _clock;
    private Label? _unlockLabel;
    private Label? _statsLabel;
    private ModernButton? _restoreButton;

    private VaultStatus _vault = new();
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    private bool _storageOk = true;
    private bool _exiting;
    private bool _trayHintShown;
    private int _ticks;

    public MainForm()
    {
        Text = "HiddenStock";
        ClientSize = new Size(1000, 748);
        MinimumSize = new Size(940, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Font(9.5f);
        DoubleBuffered = true;

        var header = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Theme.Bg };
        header.Controls.Add(new Label
        {
            Text = "HiddenStock",
            Font = Theme.Font(17f, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(32, 16)
        });
        header.Controls.Add(new Label
        {
            Text = "Сейф с датой открытия",
            Font = Theme.Font(9.5f),
            ForeColor = Theme.Muted,
            AutoSize = true,
            Location = new Point(34, 48)
        });

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.Muted,
            Font = Theme.Font(9f),
            Padding = new Padding(0, 0, 32, 0)
        };
        var statusHost = new Panel { Dock = DockStyle.Right, Width = 460, BackColor = Theme.Bg };
        statusHost.Controls.Add(_statusLabel);
        header.Controls.Add(statusHost);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 38, BackColor = Theme.Bg };
        footer.Controls.Add(new Label
        {
            Text = "Всё хранится только на этом компьютере, в зашифрованном виде.",
            ForeColor = Theme.Muted,
            Font = Theme.Font(8.5f),
            AutoSize = true,
            Location = new Point(32, 10)
        });

        _content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(32, 4, 32, 8),
            BackColor = Theme.Bg
        };

        Controls.Add(_content);
        Controls.Add(footer);
        Controls.Add(header);

        var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        _tray = new NotifyIcon { Icon = icon, Text = "HiddenStock", Visible = true };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть HiddenStock", null, (_, _) => RestoreWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => { _exiting = true; Close(); });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => RestoreWindow();

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();

        TimeGuard.Touch();
        _storageOk = AppPaths.CanWrite();
        RefreshView();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            var dark = 1;
            DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
        }
        catch { }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && !_exiting)
        {
            Hide();
            ShowInTaskbar = false;
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray.ShowBalloonTip(3000, "HiddenStock", "Программа свёрнута в трей и продолжает считать дни.", ToolTipIcon.Info);
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray.ShowBalloonTip(3000, "HiddenStock", "Программа осталась в трее. Выход — через меню иконки.", ToolTipIcon.Info);
            }
            return;
        }

        _timer.Stop();
        _tray.Visible = false;
        base.OnFormClosing(e);
    }

    internal void AllowClose()
    {
        _exiting = true;
    }

    private void RestoreWindow()
    {        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void OnTick()
    {
        _ticks++;
        if (_ticks % 60 == 0)
            TimeGuard.Touch();

        _now = DateTimeOffset.UtcNow;

        if (!_vault.Exists || _vault.Corrupt)
            return;

        var unlock = DateTimeOffset.FromUnixTimeSeconds(_vault.UnlockAt);
        var left = unlock - _now;

        if (left <= TimeSpan.Zero)
        {
            if (!_vault.Unlocked)
            {
                RefreshView();
                _tray.ShowBalloonTip(6000, "HiddenStock", "Сейф открыт. Файлы можно восстановить.", ToolTipIcon.Info);
            }
            return;
        }

        UpdateCountdown(left);
    }

    private void UpdateCountdown(TimeSpan left)
    {
        if (_bigNumber == null || _clock == null)
            return;

        var days = (int)left.TotalDays;
        _bigNumber.Text = days.ToString();
        _bigUnit!.Text = Plural(days, "день", "дня", "дней");
        _clock.Text = left.ToString(@"hh\:mm\:ss");
    }

    private static string Plural(int value, string one, string few, string many)
    {
        var mod100 = value % 100;
        var mod10 = value % 10;
        if (mod100 is >= 11 and <= 14) return many;
        return mod10 switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many
        };
    }

    private void RefreshView()
    {
        _vault = Vault.GetStatus();
        _now = DateTimeOffset.UtcNow;

        while (_content.Controls.Count > 0)
        {
            var control = _content.Controls[0];
            _content.Controls.RemoveAt(0);
            control.Dispose();
        }

        _fileList = null;
        _selectionInfo = null;
        _datePicker = null;
        _deleteOriginals = null;
        _sealButton = null;
        _progress = null;
        _progressLabel = null;
        _listPlaceholder = null;
        _bigNumber = null;
        _bigUnit = null;
        _clock = null;
        _unlockLabel = null;
        _statsLabel = null;
        _restoreButton = null;

        var time = TimeGuard.Check();

        if (!_storageOk)
        {
            _statusLabel.Text = "НЕТ ДОСТУПА К ПАПКЕ";
            _statusLabel.ForeColor = Theme.Danger;
            ShowNoStorage();
            return;
        }

        if (_vault.Corrupt)
        {
            _statusLabel.Text = "СЕЙФ ПОВРЕЖДЁН";
            _statusLabel.ForeColor = Theme.Danger;
            ShowCorrupt();
            return;
        }

        if (time.Rollback)
        {
            _statusLabel.Text = "ВРЕМЯ ИЗМЕНЕНО";
            _statusLabel.ForeColor = Theme.Danger;
            ShowRollback();
            return;
        }

        if (!_vault.Exists)
        {
            _statusLabel.Text = "СЕЙФА НЕТ";
            _statusLabel.ForeColor = Theme.Muted;
            ShowSeal();
            return;
        }

        if (_vault.Unlocked)
        {
            _statusLabel.Text = "ОТКРЫТО";
            _statusLabel.ForeColor = Theme.Ok;
            ShowUnlocked();
            return;
        }

        _statusLabel.Text = $"ЗАБЛОКИРОВАНО ДО {_vault.UnlockLocal:dd.MM.yyyy}";
        _statusLabel.ForeColor = Theme.Accent;
        ShowLocked();
    }

    private TableLayoutPanel NewLayout(params (SizeType Type, float Value)[] rows)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = rows.Length,
            BackColor = Theme.Bg,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        foreach (var (type, value) in rows)
            layout.RowStyles.Add(new RowStyle(type, value));

        return layout;
    }

    private static Label Caption(string text, Font font, Color color, ContentAlignment align = ContentAlignment.MiddleLeft) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        Dock = DockStyle.Fill,
        TextAlign = align,
        BackColor = Color.Transparent,
        Margin = new Padding(0)
    };

    private void ShowSeal()
    {
        var layout = NewLayout(
            (SizeType.Absolute, 42),
            (SizeType.Absolute, 28),
            (SizeType.Absolute, 148),
            (SizeType.Absolute, 54),
            (SizeType.Percent, 100),
            (SizeType.Absolute, 50),
            (SizeType.Absolute, 46),
            (SizeType.Absolute, 38),
            (SizeType.Absolute, 62));

        layout.Controls.Add(Caption("Новый сейф", Theme.Font(15f, FontStyle.Bold), Theme.Text), 0, 0);
        layout.Controls.Add(Caption("Добавь файлы, выбери дату — раньше неё сейф не откроется.",
            Theme.Font(9.5f), Theme.Muted), 0, 1);

        var drop = new DropZone { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
        drop.Dropped += items => AddPaths(items);
        layout.Controls.Add(drop, 0, 2);

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Theme.Bg,
            Margin = new Padding(0)
        };

        var addFiles = new ModernButton { Text = "Выбрать файлы", Accent = false, Width = 160, Height = 40, Margin = new Padding(0, 0, 10, 0) };
        addFiles.Click += (_, _) => PickFiles();
        var addFolder = new ModernButton { Text = "Выбрать папку", Accent = false, Width = 160, Height = 40, Margin = new Padding(0, 0, 10, 0) };
        addFolder.Click += (_, _) => PickFolder();
        var clear = new ModernButton { Text = "Очистить", Ghost = true, Width = 110, Height = 40 };
        clear.Click += (_, _) => { _roots.Clear(); _files.Clear(); UpdateSelection(); };

        buttonRow.Controls.Add(addFiles);
        buttonRow.Controls.Add(addFolder);
        buttonRow.Controls.Add(clear);
        layout.Controls.Add(buttonRow, 0, 3);

        var listCard = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6), Radius = 12, Fill = Theme.Surface };
        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            Font = Theme.Font(9f),
            IntegralHeight = false,
            Margin = new Padding(0)
        };
        var listPad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 10), BackColor = Theme.Surface };
        listPad.Controls.Add(list);

        var placeholder = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.Muted,
            BackColor = Theme.Surface,
            Font = Theme.Font(9f),
            Text = "Файлы не выбраны — перетащи их сюда или нажми «Выбрать файлы»"
        };
        listPad.Controls.Add(placeholder);
        placeholder.BringToFront();
        _listPlaceholder = placeholder;

        listCard.Controls.Add(listPad);
        _fileList = list;
        layout.Controls.Add(listCard, 0, 4);

        var dateRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Theme.Bg,
            Margin = new Padding(0)
        };

        dateRow.Controls.Add(new Label
        {
            Text = "Открыть после:",
            ForeColor = Theme.Text,
            Font = Theme.Font(9.5f),
            AutoSize = false,
            Width = 108,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft
        });

        var picker = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "dd.MM.yyyy  HH:mm",
            Width = 190,
            Font = Theme.Font(10f),
            Value = DateTime.Now.AddDays(30),
            MinDate = DateTime.Now.AddMinutes(2),
            Margin = new Padding(0, 2, 16, 0)
        };
        _datePicker = picker;
        dateRow.Controls.Add(picker);

        var deleteCheck = new CheckBox
        {
            Text = "Удалить исходные файлы после запечатывания",
            ForeColor = Theme.Muted,
            BackColor = Theme.Bg,
            Font = Theme.Font(9f),
            AutoSize = true,
            Checked = true,
            Margin = new Padding(0, 7, 0, 0)
        };
        _deleteOriginals = deleteCheck;
        dateRow.Controls.Add(deleteCheck);

        layout.Controls.Add(dateRow, 0, 5);

        layout.Controls.Add(Caption("Пароля нет. Восстановления нет. До выбранной даты открыть сейф невозможно.",
            Theme.Font(9f), Color.FromArgb(226, 160, 120)), 0, 6);

        var progressRow = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = new Padding(0) };
        var bar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 6,
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            ForeColor = Theme.Accent,
            BackColor = Theme.SurfaceAlt
        };
        bar.Visible = false;
        var progressText = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 22,
            ForeColor = Theme.Muted,
            Font = Theme.Font(8.5f),
            TextAlign = ContentAlignment.MiddleLeft
        };
        progressRow.Controls.Add(progressText);
        progressRow.Controls.Add(bar);
        _progress = bar;
        _progressLabel = progressText;
        layout.Controls.Add(progressRow, 0, 7);

        var sealRow = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = new Padding(0) };
        var seal = new ModernButton
        {
            Text = "Запечатать сейф",
            Accent = true,
            Width = 230,
            Height = 46,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        seal.Location = new Point(sealRow.Width - seal.Width, 6);
        sealRow.Resize += (_, _) => seal.Location = new Point(sealRow.Width - seal.Width, 6);
        seal.Click += async (_, _) => await SealAsync();
        _sealButton = seal;
        sealRow.Controls.Add(seal);
        layout.Controls.Add(sealRow, 0, 8);

        _content.Controls.Add(layout);
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        if (_fileList == null)
            return;

        _fileList.BeginUpdate();
        _fileList.Items.Clear();
        foreach (var file in _files.Take(5000))
            _fileList.Items.Add(file);
        _fileList.EndUpdate();

        if (_selectionInfo != null)
            _selectionInfo.Text = _files.Count == 0 ? "Пока пусто" : $"{_files.Count} файлов";

        if (_listPlaceholder != null)
        {
            var empty = _files.Count == 0;
            _listPlaceholder.Visible = empty;
            if (_fileList != null)
                _fileList.Visible = !empty;
        }
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                continue;

            if (_roots.Any(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            _roots.Add(path);

            var expanded = new List<string>();
            if (File.Exists(path))
            {
                expanded.Add(path);
            }
            else
            {
                try
                {
                    var options = new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    };
                    expanded.AddRange(Directory.EnumerateFiles(path, "*", options));
                }
                catch { }
            }

            foreach (var file in expanded)
            {
                if (!_files.Any(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase)))
                    _files.Add(file);
            }
        }

        UpdateSelection();
    }

    private void PickFiles()
    {
        using var dialog = new OpenFileDialog { Multiselect = true, Title = "Выбери файлы для сейфа" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            AddPaths(dialog.FileNames);
    }

    private void PickFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Выбери папку для сейфа", ShowNewFolderButton = false };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            AddPaths(new[] { dialog.SelectedPath });
    }

    private async Task SealAsync()
    {
        if (_roots.Count == 0 || _files.Count == 0)
        {
            MessageBox.Show(this, "Сначала добавь файлы.", "Пусто", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_datePicker == null)
            return;

        if (TimeGuard.Check().Rollback)
        {
            MessageBox.Show(this, "Время на компьютере переведено назад. Запечатывание заблокировано.",
                "Проверка времени", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var unlockLocal = _datePicker.Value;
        var unlock = new DateTimeOffset(unlockLocal).ToUniversalTime();

        if (unlock <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            MessageBox.Show(this, "Дата должна быть в будущем.", "Дата", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var size = _files.Sum(f => new FileInfo(f).Length);
        var question = $"Запечатать {_files.Count} файлов ({FormatSize(size)}) до {unlockLocal:dd.MM.yyyy HH:mm}?\n\n" +
                       "Пароля нет, отменить нельзя. Раньше этой даты файлы не открыть.";

        if (MessageBox.Show(this, question, "Запечатать сейф", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        SetBusy(true);
        var progress = new Progress<ProgressInfo>(p =>
        {
            if (_progress != null) _progress.Value = Math.Clamp(p.Percent, 0, 100);
            if (_progressLabel != null) _progressLabel.Text = p.Message;
        });

        try
        {
            var report = await Vault.SealAsync(_roots, unlock, _deleteOriginals?.Checked ?? true, progress, CancellationToken.None);
            MessageBox.Show(this,
                $"Сейф создан.\n\nФайлов: {report.Files}\nОбъём: {FormatSize(report.Bytes)}\nОткроется: {report.UnlockAt.ToLocalTime():dd.MM.yyyy HH:mm}",
                "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _roots.Clear();
            _files.Clear();
            RefreshView();
        }
        catch (UnauthorizedAccessException ex)
        {
            _storageOk = AppPaths.CanWrite();
            MessageBox.Show(this,
                "Нет доступа к папке данных.\n\n" + AppPaths.Root +
                "\n\nСкопируй HiddenStock.exe на рабочий стол и запусти из Проводника.\n\n" + ex.Message,
                "Доступ запрещён", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshView();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось запечатать: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowNoStorage()
    {
        var layout = NewLayout(
            (SizeType.Percent, 50),
            (SizeType.Absolute, 56),
            (SizeType.Absolute, 180),
            (SizeType.Percent, 50),
            (SizeType.Absolute, 60));

        layout.Controls.Add(Caption("Нет доступа к папке данных",
            Theme.Font(16f, FontStyle.Bold), Theme.Danger, ContentAlignment.MiddleCenter), 0, 1);

        var text = "Windows не разрешает программе писать в\n" + AppPaths.Root + "\n\n" +
                   "Обычно это значит, что exe запущен в ограниченной среде (песочнице) " +
                   "или антивирус заблокировал папку.\n\n" +
                   "Скопируй HiddenStock.exe на рабочий стол и запусти двойным кликом из Проводника.";

        layout.Controls.Add(Caption(text, Theme.Font(9.5f), Theme.Muted, ContentAlignment.TopCenter), 0, 2);

        var retry = new ModernButton
        {
            Text = "Повторить проверку",
            Accent = true,
            Width = 220,
            Height = 44,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        retry.Click += (_, _) =>
        {
            _storageOk = AppPaths.CanWrite();
            RefreshView();
        };
        layout.Controls.Add(retry, 0, 4);

        _content.Controls.Add(layout);
    }

    private void ShowLocked()
    {
        var layout = NewLayout((SizeType.Percent, 50), (SizeType.Absolute, 310), (SizeType.Absolute, 70), (SizeType.Percent, 50));

        var card = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), Radius = 20, Fill = Theme.Surface };
        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Theme.Surface,
            Padding = new Padding(20)
        };
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var caption = Caption("ОТКРОЕТСЯ ЧЕРЕЗ", Theme.Font(9f, FontStyle.Bold), Theme.Muted, ContentAlignment.MiddleCenter);
        caption.BackColor = Theme.Surface;
        inner.Controls.Add(caption, 0, 0);

        var digits = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Anchor = AnchorStyles.None,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };

        _bigNumber = new Label
        {
            Text = "0",
            Font = Theme.Font(56f, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };
        _bigUnit = new Label
        {
            Text = "дней",
            Font = Theme.Font(15f),
            ForeColor = Theme.Muted,
            AutoSize = true,
            BackColor = Theme.Surface,
            Margin = new Padding(10, 34, 0, 0)
        };
        digits.Controls.Add(_bigNumber);
        digits.Controls.Add(_bigUnit);
        inner.Controls.Add(digits, 0, 1);

        _clock = new Label
        {
            Text = "00:00:00",
            Font = Theme.Font(19f),
            ForeColor = Theme.Text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Theme.Surface
        };
        inner.Controls.Add(_clock, 0, 2);

        _unlockLabel = new Label
        {
            Text = _vault.UnlockLocal.ToString("dd MMMM yyyy, HH:mm"),
            Font = Theme.Font(10f),
            ForeColor = Theme.Muted,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopCenter,
            BackColor = Theme.Surface
        };
        inner.Controls.Add(_unlockLabel, 0, 3);

        _statsLabel = new Label
        {
            Text = $"{_vault.FileCount} {Plural(_vault.FileCount, "файл", "файла", "файлов")} · {FormatSize(_vault.TotalBytes)}",
            Font = Theme.Font(9.5f),
            ForeColor = Theme.Muted,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Theme.Surface
        };
        inner.Controls.Add(_statsLabel, 0, 4);

        card.Controls.Add(inner);
        layout.Controls.Add(card, 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Theme.Bg,
            Margin = new Padding(0)
        };

        _restoreButton = new ModernButton
        {
            Text = "Восстановить файлы",
            Accent = true,
            Width = 220,
            Height = 46,
            Enabled = false,
            Margin = new Padding(0, 8, 12, 0)
        };
        actions.Controls.Add(_restoreButton);

        var erase = new ModernButton
        {
            Text = "Уничтожить сейф",
            Ghost = true,
            Danger = true,
            Width = 190,
            Height = 46,
            Margin = new Padding(0, 8, 0, 0)
        };
        erase.Click += (_, _) => EraseVault();
        actions.Controls.Add(erase);
        layout.Controls.Add(actions, 0, 2);

        _content.Controls.Add(layout);
        UpdateCountdown(DateTimeOffset.FromUnixTimeSeconds(_vault.UnlockAt) - _now);
    }

    private void ShowUnlocked()
    {
        var layout = NewLayout((SizeType.Percent, 50), (SizeType.Absolute, 290), (SizeType.Absolute, 70), (SizeType.Percent, 50));

        var card = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), Radius = 20, Fill = Theme.Surface };
        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Theme.Surface,
            Padding = new Padding(24)
        };
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        inner.Controls.Add(Caption("СЕЙФ ОТКРЫТ", Theme.Font(11f, FontStyle.Bold), Theme.Ok, ContentAlignment.MiddleCenter), 0, 0);

        var big = new Label
        {
            Text = "Файлы доступны",
            Font = Theme.Font(26f, FontStyle.Bold),
            ForeColor = Theme.Text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Theme.Surface
        };
        inner.Controls.Add(big, 0, 1);

        inner.Controls.Add(new Label
        {
            Text = $"{_vault.FileCount} {Plural(_vault.FileCount, "файл", "файла", "файлов")} · {FormatSize(_vault.TotalBytes)} · запечатан {_vault.CreatedLocal:dd.MM.yyyy}",
            Font = Theme.Font(9.5f),
            ForeColor = Theme.Muted,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Theme.Surface
        }, 0, 2);

        inner.Controls.Add(new Label
        {
            Text = "Нажми «Восстановить», чтобы вернуть файлы в выбранную папку.",
            Font = Theme.Font(9.5f),
            ForeColor = Theme.Muted,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopCenter,
            BackColor = Theme.Surface
        }, 0, 3);

        card.Controls.Add(inner);
        layout.Controls.Add(card, 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Theme.Bg,
            Margin = new Padding(0)
        };

        _restoreButton = new ModernButton
        {
            Text = "Восстановить файлы",
            Accent = true,
            Width = 230,
            Height = 46,
            Margin = new Padding(0, 8, 12, 0)
        };
        _restoreButton.Click += async (_, _) => await UnsealAsync();
        actions.Controls.Add(_restoreButton);

        var erase = new ModernButton
        {
            Text = "Уничтожить сейф",
            Ghost = true,
            Danger = true,
            Width = 190,
            Height = 46,
            Margin = new Padding(0, 8, 0, 0)
        };
        erase.Click += (_, _) => EraseVault();
        actions.Controls.Add(erase);
        layout.Controls.Add(actions, 0, 2);

        _content.Controls.Add(layout);
    }

    private void ShowCorrupt()
    {
        var layout = NewLayout(
            (SizeType.Percent, 50),
            (SizeType.Absolute, 52),
            (SizeType.Absolute, 70),
            (SizeType.Percent, 50),
            (SizeType.Absolute, 70));

        layout.Controls.Add(Caption("Сейф повреждён", Theme.Font(20f, FontStyle.Bold), Theme.Danger, ContentAlignment.MiddleCenter), 0, 1);

        var error = Caption(_vault.Error, Theme.Font(9.5f), Theme.Muted, ContentAlignment.TopCenter);
        error.Padding = new Padding(40, 0, 40, 0);
        layout.Controls.Add(error, 0, 2);

        var erase = new ModernButton
        {
            Text = "Удалить повреждённый сейф",
            Ghost = true,
            Danger = true,
            Width = 260,
            Height = 44,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        erase.Click += (_, _) => EraseVault();
        layout.Controls.Add(erase, 0, 4);

        _content.Controls.Add(layout);
    }

    private void ShowRollback()
    {
        var layout = NewLayout((SizeType.Percent, 50), (SizeType.Absolute, 220), (SizeType.Percent, 50));

        var text = "Часы компьютера переведены назад.\n\n" +
                   "Программа не знает, какое время настоящее, поэтому ничего не открывает.\n" +
                   "Верни правильное время и перезапусти HiddenStock.";
        layout.Controls.Add(Caption(text, Theme.Font(11f), Theme.Danger, ContentAlignment.MiddleCenter), 0, 1);

        _content.Controls.Add(layout);
    }

    private async Task UnsealAsync()
    {
        if (TimeGuard.Check().Rollback)
        {
            MessageBox.Show(this, "Время на компьютере переведено назад. Восстановление заблокировано.",
                "Проверка времени", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Куда восстановить файлы",
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        SetBusy(true);
        if (_restoreButton != null)
            _restoreButton.Text = "Восстанавливаю...";

        try
        {
            var report = await Vault.UnsealAsync(dialog.SelectedPath, null, CancellationToken.None);
            MessageBox.Show(this,
                $"Восстановлено.\n\nФайлов: {report.Files}\nОбъём: {FormatSize(report.Bytes)}\nПапка: {report.Destination}",
                "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);

            if (MessageBox.Show(this, "Удалить сейф? Файлы останутся в выбранной папке.",
                    "Сейф больше не нужен?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Vault.Erase();
            }

            RefreshView();
        }
        catch (UnauthorizedAccessException ex)
        {
            _storageOk = AppPaths.CanWrite();
            MessageBox.Show(this,
                "Нет доступа к папке данных.\n\n" + AppPaths.Root +
                "\n\nСкопируй HiddenStock.exe на рабочий стол и запусти из Проводника.\n\n" + ex.Message,
                "Доступ запрещён", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshView();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось восстановить: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            if (_restoreButton != null)
                _restoreButton.Text = "Восстановить файлы";
        }
    }

    private void EraseVault()
    {
        using var prompt = new PromptForm(
            "Уничтожить сейф",
            "Файлы будут потеряны навсегда, восстановить их будет нельзя. Напиши УДАЛИТЬ для подтверждения.",
            "УДАЛИТЬ");

        if (prompt.ShowDialog(this) != DialogResult.OK)
            return;

        Vault.Erase();
        RefreshView();
    }

    private void SetBusy(bool busy)
    {
        if (_sealButton != null) _sealButton.Enabled = !busy;
        if (_restoreButton != null) _restoreButton.Enabled = !busy;
        if (_progress != null) _progress.Visible = busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }
}
