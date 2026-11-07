// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 inkurant-crypto

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace FiestaResolutionEditor
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new EditorForm());
            }
            catch (Exception ex)
            {
                bool english = !String.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "de", StringComparison.OrdinalIgnoreCase);
                MessageBox.Show((english ? "The editor could not be started." : "Der Editor konnte nicht gestartet werden.") +
                    Environment.NewLine + Environment.NewLine + ex.ToString(),
                    "Fiesta Resolution Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class ResolutionPreset
    {
        public readonly int Width;
        public readonly int Height;
        public string Label;
        public readonly bool IsCustom;

        public ResolutionPreset(int width, int height, string label)
        {
            Width = width;
            Height = height;
            Label = label;
            IsCustom = false;
        }

        private ResolutionPreset()
        {
            Label = "Benutzerdefiniert";
            IsCustom = true;
        }

        public static ResolutionPreset Custom()
        {
            return new ResolutionPreset();
        }

        public override string ToString()
        {
            return Label;
        }
    }

    internal sealed class ThemeButton : Button
    {
        private readonly Color _top;
        private readonly Color _bottom;
        private readonly Color _border;
        private bool _hover;

        public ThemeButton(string text, Color top, Color bottom, Color border)
        {
            Text = text;
            _top = top;
            _bottom = bottom;
            _border = border;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = bottom;
            ForeColor = Color.FromArgb(255, 239, 190);
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            MouseEnter += delegate { _hover = true; Invalidate(); };
            MouseLeave += delegate { _hover = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle r = ClientRectangle;
            r.Width -= 1;
            r.Height -= 1;
            Color top = _hover ? Lighten(_top, 18) : _top;
            Color bottom = _hover ? Lighten(_bottom, 14) : _bottom;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (LinearGradientBrush fill = new LinearGradientBrush(r, top, bottom, 90f))
            using (Pen edge = new Pen(_border, 1f))
            {
                e.Graphics.FillPath(fill, RoundedRect(r, 5));
                e.Graphics.DrawPath(edge, RoundedRect(r, 5));
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, r, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (Focused)
            {
                Rectangle focus = Rectangle.Inflate(r, -5, -5);
                ControlPaint.DrawFocusRectangle(e.Graphics, focus);
            }
        }

        private static Color Lighten(Color c, int amount)
        {
            return Color.FromArgb(c.A,
                Math.Min(255, c.R + amount),
                Math.Min(255, c.G + amount),
                Math.Min(255, c.B + amount));
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    internal sealed class EditorForm : Form
    {
        private const int SupportedLength = 68;
        private const int WidthOffset = 39;
        private const int HeightOffset = 43;
        private const int WindowModeOffset = 47;

        private static readonly Color Night = Color.FromArgb(17, 12, 27);
        private static readonly Color Card = Color.FromArgb(35, 22, 47);
        private static readonly Color CardLight = Color.FromArgb(48, 29, 61);
        private static readonly Color Gold = Color.FromArgb(239, 204, 116);
        private static readonly Color GoldSoft = Color.FromArgb(199, 169, 111);
        private static readonly Color Magenta = Color.FromArgb(133, 39, 112);
        private static readonly Color Violet = Color.FromArgb(76, 35, 91);
        private static readonly Color WhiteText = Color.FromArgb(241, 231, 238);

        private readonly TextBox _pathBox;
        private readonly Label _fileState;
        private readonly Label _currentResolution;
        private readonly Label _status;
        private readonly ComboBox _presetBox;
        private readonly ComboBox _modeBox;
        private readonly NumericUpDown _widthBox;
        private readonly NumericUpDown _heightBox;
        private readonly CheckBox _readOnlyAfterSave;
        private readonly ThemeButton _saveButton;
        private readonly ThemeButton _restoreButton;
        private readonly ThemeButton _readOnlyButton;
        private readonly ThemeButton _unlockButton;
        private readonly ToolTip _tips;
        private ComboBox _languageBox;
        private Label _languageCaption;
        private ContextMenuStrip _restoreMenu;
        private ToolStripMenuItem _saveDefaultItem;
        private ToolStripMenuItem _restoreDefaultItem;
        private ToolStripMenuItem _restoreBackupItem;

        private string _loadedPath;
        private int _loadedWidth;
        private int _loadedHeight;
        private bool _loadedWindowed;
        private bool _loadedReadOnly;
        private bool _updatingResolution;
        private bool _english;
        private string _loadedFingerprint;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DevMode devMode);

        public EditorForm()
        {
            _english = !String.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "de", StringComparison.OrdinalIgnoreCase);
            Text = L("Fiesta Online – Auflösungswerkstatt 1.7", "Fiesta Online – Resolution Editor 1.7");
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            ClientSize = new Size(850, 720);
            BackColor = Night;
            ForeColor = WhiteText;
            Font = new Font("Segoe UI", 9f);
            DoubleBuffered = true;

            _tips = new ToolTip();

            Panel header = BuildHeader();
            Controls.Add(header);

            Panel fileCard = CreateCard("01   SPIELDATEI", new Rectangle(24, 128, 802, 132));
            Controls.Add(fileCard);

            Label pathCaption = MakeLabel("Option.mco auswählen", GoldSoft, 9f, true);
            pathCaption.Location = new Point(20, 39);
            pathCaption.Size = new Size(260, 20);
            fileCard.Controls.Add(pathCaption);

            _pathBox = new TextBox();
            _pathBox.Location = new Point(20, 64);
            _pathBox.Size = new Size(420, 29);
            StyleTextBox(_pathBox);
            _pathBox.Text = @"C:\Program Files (x86)\Glyph\Games\FiestaOnline\DE\ressystem\Option.mco";
            fileCard.Controls.Add(_pathBox);

            ThemeButton gameFolderButton = CreateButton("SPIELORDNER", 450, 62, 110, 32, false);
            gameFolderButton.Click += SelectGameFolder;
            fileCard.Controls.Add(gameFolderButton);

            ThemeButton browseButton = CreateButton("DATEI WÄHLEN", 570, 62, 116, 32, false);
            browseButton.Click += BrowseForFile;
            fileCard.Controls.Add(browseButton);

            ThemeButton loadButton = CreateButton("LADEN", 696, 62, 84, 32, false);
            loadButton.Click += delegate { LoadSelectedFile(); };
            fileCard.Controls.Add(loadButton);

            _fileState = MakeLabel("Noch keine Datei geladen", Color.FromArgb(193, 172, 191), 8.5f, false);
            _fileState.Location = new Point(20, 101);
            _fileState.Size = new Size(750, 20);
            fileCard.Controls.Add(_fileState);

            Panel resolutionCard = CreateCard("02   AUFLÖSUNG", new Rectangle(24, 274, 802, 236));
            Controls.Add(resolutionCard);

            Label presetCaption = MakeLabel("Gängige Auflösungen", GoldSoft, 9f, true);
            presetCaption.Location = new Point(20, 40);
            presetCaption.Size = new Size(300, 20);
            resolutionCard.Controls.Add(presetCaption);

            _presetBox = new ComboBox();
            _presetBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _presetBox.FlatStyle = FlatStyle.Flat;
            _presetBox.BackColor = CardLight;
            _presetBox.ForeColor = WhiteText;
            _presetBox.Font = new Font("Segoe UI", 10f);
            _presetBox.Location = new Point(20, 64);
            _presetBox.Size = new Size(400, 30);
            AddPresets();
            _presetBox.SelectedIndexChanged += PresetChanged;
            resolutionCard.Controls.Add(_presetBox);

            Panel currentBadge = new Panel();
            currentBadge.Location = new Point(452, 49);
            currentBadge.Size = new Size(328, 62);
            currentBadge.BackColor = CardLight;
            currentBadge.Paint += PaintBadge;
            resolutionCard.Controls.Add(currentBadge);

            Label currentCaption = MakeLabel("AKTUELL IN DER DATEI", GoldSoft, 8f, true);
            currentCaption.Location = new Point(14, 8);
            currentCaption.Size = new Size(290, 18);
            currentBadge.Controls.Add(currentCaption);

            _currentResolution = MakeLabel("— × —", Gold, 17f, true);
            _currentResolution.Tag = null;
            _currentResolution.Location = new Point(14, 27);
            _currentResolution.Size = new Size(290, 28);
            currentBadge.Controls.Add(_currentResolution);

            Label customCaption = MakeLabel("Eigene Werte", GoldSoft, 9f, true);
            customCaption.Location = new Point(20, 119);
            customCaption.Size = new Size(300, 20);
            resolutionCard.Controls.Add(customCaption);

            Label widthCaption = MakeLabel("Breite", WhiteText, 8.5f, false);
            widthCaption.Location = new Point(20, 146);
            widthCaption.Size = new Size(120, 18);
            resolutionCard.Controls.Add(widthCaption);

            _widthBox = CreateNumber(20, 167, 155, 8192, 1920);
            _widthBox.ValueChanged += CustomResolutionChanged;
            resolutionCard.Controls.Add(_widthBox);

            Label heightCaption = MakeLabel("Höhe", WhiteText, 8.5f, false);
            heightCaption.Location = new Point(198, 146);
            heightCaption.Size = new Size(120, 18);
            resolutionCard.Controls.Add(heightCaption);

            _heightBox = CreateNumber(198, 167, 155, 4320, 1080);
            _heightBox.Minimum = 480;
            _heightBox.ValueChanged += CustomResolutionChanged;
            resolutionCard.Controls.Add(_heightBox);

            Label modeCaption = MakeLabel("Anzeigemodus", GoldSoft, 9f, true);
            modeCaption.Location = new Point(452, 119);
            modeCaption.Size = new Size(300, 20);
            resolutionCard.Controls.Add(modeCaption);

            _modeBox = new ComboBox();
            _modeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _modeBox.FlatStyle = FlatStyle.Flat;
            _modeBox.BackColor = CardLight;
            _modeBox.ForeColor = WhiteText;
            _modeBox.Font = new Font("Segoe UI", 10f);
            _modeBox.Location = new Point(452, 144);
            _modeBox.Size = new Size(300, 30);
            _modeBox.Items.Add("Fenstermodus");
            _modeBox.Items.Add("Vollbildmodus");
            _modeBox.SelectedIndex = 0;
            _modeBox.SelectedIndexChanged += delegate { if (!_updatingResolution) UpdatePreviewStatus(); };
            resolutionCard.Controls.Add(_modeBox);
            _tips.SetToolTip(_modeBox, "Der Modus wird in Option.mco gespeichert und beim nächsten Spielstart übernommen.");

            Label modeNote = MakeLabel("Wird beim nächsten Spielstart übernommen.", Color.FromArgb(193, 172, 191), 8.5f, false);
            modeNote.Location = new Point(452, 183);
            modeNote.Size = new Size(330, 42);
            resolutionCard.Controls.Add(modeNote);

            Panel protectionCard = CreateCard("03   SCHREIBSCHUTZ & SICHERN", new Rectangle(24, 524, 802, 126));
            Controls.Add(protectionCard);

            _readOnlyAfterSave = new CheckBox();
            _readOnlyAfterSave.Text = "Nach dem Speichern schreibgeschützt lassen";
            _readOnlyAfterSave.Tag = _readOnlyAfterSave.Text;
            _readOnlyAfterSave.Location = new Point(20, 42);
            _readOnlyAfterSave.Size = new Size(350, 24);
            _readOnlyAfterSave.ForeColor = WhiteText;
            _readOnlyAfterSave.BackColor = Color.Transparent;
            _readOnlyAfterSave.CheckedChanged += delegate { UpdateProtectionButtons(); };
            protectionCard.Controls.Add(_readOnlyAfterSave);
            _tips.SetToolTip(_readOnlyAfterSave, "Fiesta kann dann Änderungen an dieser Datei möglicherweise nicht speichern.");

            _readOnlyButton = CreateButton("SCHREIBSCHUTZ AN", 388, 38, 178, 34, false);
            _readOnlyButton.Click += delegate { SetReadOnly(true); };
            _readOnlyButton.Enabled = false;
            protectionCard.Controls.Add(_readOnlyButton);

            _unlockButton = CreateButton("SCHREIBSCHUTZ AUS", 578, 38, 202, 34, false);
            _unlockButton.Click += delegate { SetReadOnly(false); };
            _unlockButton.Enabled = false;
            protectionCard.Controls.Add(_unlockButton);

            _status = MakeLabel("Offline & privat  •  Keine Netzwerkverbindung  •  Sicherung vor jeder Änderung", Color.FromArgb(164, 205, 161), 8.5f, false);
            _status.Location = new Point(20, 82);
            _status.Size = new Size(760, 22);
            protectionCard.Controls.Add(_status);

            Label footer = MakeLabel("FIESTA ONLINE     ◆     LOKALER KONFIGURATIONS-EDITOR", Color.FromArgb(156, 132, 161), 8f, true);
            footer.Location = new Point(30, 675);
            footer.Size = new Size(350, 24);
            Controls.Add(footer);

            _restoreButton = CreateButton("ZURÜCKSETZEN", 386, 666, 184, 42, false);
            _restoreButton.Enabled = false;
            _restoreButton.Click += ShowRestoreMenu;
            Controls.Add(_restoreButton);

            _saveButton = CreateButton("EINSTELLUNGEN SPEICHERN", 586, 666, 240, 42, true);
            _saveButton.Enabled = false;
            _saveButton.Click += delegate { SaveResolution(); };
            Controls.Add(_saveButton);

            _languageBox.SelectedIndexChanged += LanguageChanged;
            InitializeRestoreMenu();
            ApplyLanguage();
            TryLoadDefaultFile();
        }

        private string L(string german, string english)
        {
            return _english ? english : german;
        }

        private void LanguageChanged(object sender, EventArgs e)
        {
            if (_languageBox.SelectedIndex < 0)
                return;

            _english = _languageBox.SelectedIndex == 1;
            ApplyLanguage();
            if (_loadedPath != null)
            {
                RefreshFileState();
                UpdatePreviewStatus();
            }
        }

        private void ApplyLanguage()
        {
            Text = L("Fiesta Online – Auflösungswerkstatt 1.7", "Fiesta Online – Resolution Editor 1.7");
            ApplyLanguageToControls(this);

            int selectedMode = _modeBox.SelectedIndex;
            bool wasUpdating = _updatingResolution;
            _updatingResolution = true;
            _modeBox.BeginUpdate();
            _modeBox.Items.Clear();
            _modeBox.Items.Add(L("Fenstermodus", "Windowed"));
            _modeBox.Items.Add(L("Vollbildmodus", "Fullscreen"));
            _modeBox.SelectedIndex = selectedMode < 0 ? 0 : selectedMode;
            _modeBox.EndUpdate();
            _updatingResolution = wasUpdating;

            for (int i = 0; i < _presetBox.Items.Count; i++)
            {
                ResolutionPreset preset = _presetBox.Items[i] as ResolutionPreset;
                if (preset != null && preset.IsCustom)
                    preset.Label = L("Benutzerdefiniert", "Custom");
            }
            _presetBox.Refresh();

            _tips.SetToolTip(_modeBox, L(
                "Der Modus wird in Option.mco gespeichert und beim nächsten Spielstart übernommen.",
                "The mode is saved in Option.mco and takes effect the next time you start the game."));
            _tips.SetToolTip(_readOnlyAfterSave, L(
                "Fiesta kann dann Änderungen an dieser Datei möglicherweise nicht speichern.",
                "Fiesta may be unable to save changes to this file while it is read-only."));
            if (_saveDefaultItem != null)
                _saveDefaultItem.Text = L("Aktuelle Datei als Standard speichern", "Save current file as default");
            if (_restoreDefaultItem != null)
                _restoreDefaultItem.Text = L("Standard wiederherstellen", "Restore saved default");
            if (_restoreBackupItem != null)
                _restoreBackupItem.Text = L("Sicherung auswählen…", "Choose a backup…");
        }

        private void ApplyLanguageToControls(Control parent)
        {
            string germanText = parent.Tag as string;
            if (germanText != null)
                parent.Text = TranslateUiText(germanText);

            foreach (Control child in parent.Controls)
                ApplyLanguageToControls(child);
        }

        private string TranslateUiText(string germanText)
        {
            if (!_english)
                return germanText;

            switch (germanText)
            {
                case "SPRACHE": return "LANGUAGE";
                case "AUFLÖSUNGSWERKSTATT     •     OPTION.MCO": return "RESOLUTION EDITOR     •     OPTION.MCO";
                case "01   SPIELDATEI": return "01   GAME FILE";
                case "Option.mco auswählen": return "Select Option.mco";
                case "DATEI WÄHLEN": return "BROWSE";
                case "SPIELORDNER": return "GAME FOLDER";
                case "LADEN": return "LOAD";
                case "Noch keine Datei geladen": return "No file loaded";
                case "02   AUFLÖSUNG": return "02   RESOLUTION";
                case "Gängige Auflösungen": return "Common resolutions";
                case "AKTUELL IN DER DATEI": return "CURRENT IN FILE";
                case "Eigene Werte": return "Custom values";
                case "Breite": return "Width";
                case "Höhe": return "Height";
                case "Anzeigemodus": return "Display mode";
                case "Wird beim nächsten Spielstart übernommen.": return "Takes effect the next time you start the game.";
                case "03   SCHREIBSCHUTZ & SICHERN": return "03   READ-ONLY & BACKUP";
                case "Nach dem Speichern schreibgeschützt lassen": return "Keep read-only after saving";
                case "SCHREIBSCHUTZ AN": return "ENABLE READ-ONLY";
                case "SCHREIBSCHUTZ AUS": return "DISABLE READ-ONLY";
                case "Offline & privat  •  Keine Netzwerkverbindung  •  Sicherung vor jeder Änderung": return "Private & local  •  No network connection  •  Backup before changes";
                case "FIESTA ONLINE     ◆     LOKALER KONFIGURATIONS-EDITOR": return "FIESTA ONLINE     ◆     LOCAL CONFIGURATION EDITOR";
                case "EINSTELLUNGEN SPEICHERN": return "SAVE SETTINGS";
                case "ZURÜCKSETZEN": return "RESTORE";
                default: return germanText;
            }
        }

        private void RefreshFileState()
        {
            string access = _loadedReadOnly
                ? L("schreibgeschützt", "read-only")
                : L("beschreibbar", "writable");
            _fileState.Text = Path.GetFileName(_loadedPath) + "   ·   " + SupportedLength +
                L(" Byte   ·   ", " bytes   ·   ") + ModeName(_loadedWindowed) + "   ·   " + access;
        }

        private void InitializeRestoreMenu()
        {
            _restoreMenu = new ContextMenuStrip();
            _restoreMenu.BackColor = CardLight;
            _restoreMenu.ForeColor = WhiteText;
            _saveDefaultItem = new ToolStripMenuItem();
            _restoreDefaultItem = new ToolStripMenuItem();
            _restoreBackupItem = new ToolStripMenuItem();
            _saveDefaultItem.Click += delegate { SaveDefaultConfiguration(); };
            _restoreDefaultItem.Click += delegate { RestoreDefaultConfiguration(); };
            _restoreBackupItem.Click += delegate { RestoreBackupFromDialog(); };
            _restoreMenu.Items.Add(_saveDefaultItem);
            _restoreMenu.Items.Add(_restoreDefaultItem);
            _restoreMenu.Items.Add(new ToolStripSeparator());
            _restoreMenu.Items.Add(_restoreBackupItem);
        }

        private void ShowRestoreMenu(object sender, EventArgs e)
        {
            if (_loadedPath == null)
                return;
            _restoreDefaultItem.Enabled = File.Exists(GetDefaultConfigPath());
            _restoreMenu.Show(_restoreButton, new Point(0, _restoreButton.Height));
        }

        private void SaveDefaultConfiguration()
        {
            try
            {
                if (_loadedPath == null)
                    throw new InvalidOperationException(L("Lade zuerst eine gültige Option.mco-Datei.", "Load a valid Option.mco file first."));
                EnsureGameClosed();
                byte[] data = File.ReadAllBytes(_loadedPath);
                ValidateOptionData(data);
                EnsureFileHasNotChanged(data);

                string defaultPath = GetDefaultConfigPath();
                string folder = Path.GetDirectoryName(defaultPath);
                Directory.CreateDirectory(folder);
                if (File.Exists(defaultPath))
                {
                    DialogResult overwrite = MessageBox.Show(this,
                        L("Der gespeicherte Standard wird ersetzt. Fortfahren?", "Replace the saved default configuration?"),
                        L("Standard ersetzen", "Replace default"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (overwrite != DialogResult.Yes)
                        return;
                    File.Copy(defaultPath, defaultPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"), false);
                }

                File.WriteAllBytes(defaultPath, data);
                _status.Text = L("Standardkonfiguration lokal gespeichert.", "Default configuration saved locally.");
                MessageBox.Show(this, _status.Text, L("Standard gespeichert", "Default saved"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowSaveError(ex);
            }
        }

        private void RestoreDefaultConfiguration()
        {
            try
            {
                if (_loadedPath == null)
                    throw new InvalidOperationException(L("Lade zuerst eine gültige Option.mco-Datei.", "Load a valid Option.mco file first."));
                string defaultPath = GetDefaultConfigPath();
                if (!File.Exists(defaultPath))
                {
                    MessageBox.Show(this,
                        L("Für diese Spielinstallation wurde noch kein Standard gespeichert.",
                          "No default has been saved for this game installation yet."),
                        L("Kein Standard vorhanden", "No saved default"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                RestoreFromSource(defaultPath, L("gespeicherten Standard", "saved default"));
            }
            catch (Exception ex)
            {
                ShowSaveError(ex);
            }
        }

        private void RestoreBackupFromDialog()
        {
            try
            {
                if (_loadedPath == null)
                    throw new InvalidOperationException(L("Lade zuerst eine gültige Option.mco-Datei.", "Load a valid Option.mco file first."));
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = L("Fiesta-Sicherung auswählen", "Choose a Fiesta backup");
                    dialog.Filter = L("Fiesta-Sicherungen|Option.mco.bak-*|Alle Dateien (*.*)|*.*",
                        "Fiesta backups|Option.mco.bak-*|All files (*.*)|*.*");
                    dialog.CheckFileExists = true;
                    dialog.Multiselect = false;
                    dialog.InitialDirectory = Path.GetDirectoryName(_loadedPath);
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        RestoreFromSource(dialog.FileName, L("ausgewählte Sicherung", "selected backup"));
                }
            }
            catch (Exception ex)
            {
                ShowSaveError(ex);
            }
        }

        private void RestoreFromSource(string sourcePath, string sourceDescription)
        {
            string backup = null;
            string temp = null;
            bool replaced = false;
            FileAttributes originalAttributes = 0;
            bool haveOriginalAttributes = false;
            try
            {
                if (_loadedPath == null)
                    throw new InvalidOperationException(L("Lade zuerst eine gültige Option.mco-Datei.", "Load a valid Option.mco file first."));
                EnsureGameClosed();

                byte[] current = File.ReadAllBytes(_loadedPath);
                ValidateOptionData(current);
                EnsureFileHasNotChanged(current);
                byte[] replacement = File.ReadAllBytes(sourcePath);
                ValidateOptionData(replacement);

                if (ByteArraysEqual(current, replacement))
                {
                    _status.Text = L("Die gewählte Konfiguration entspricht bereits der aktuellen Datei.",
                        "The selected configuration already matches the current file.");
                    return;
                }

                string preview = BuildRestorePreview(current, replacement, sourceDescription);
                if (MessageBox.Show(this, preview, L("Wiederherstellung prüfen", "Review restoration"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;

                originalAttributes = File.GetAttributes(_loadedPath);
                haveOriginalAttributes = true;
                backup = _loadedPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                temp = _loadedPath + ".tmp-" + Guid.NewGuid().ToString("N");
                File.Copy(_loadedPath, backup, false);
                File.WriteAllBytes(temp, replacement);
                if ((originalAttributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(_loadedPath, originalAttributes & ~FileAttributes.ReadOnly);

                File.Replace(temp, _loadedPath, null);
                temp = null;
                replaced = true;
                File.SetAttributes(_loadedPath, originalAttributes);

                byte[] restored = File.ReadAllBytes(_loadedPath);
                if (!ByteArraysEqual(restored, replacement))
                    throw new IOException(L("Die Prüfung nach der Wiederherstellung ist fehlgeschlagen.",
                        "The post-restore check failed."));
                UpdateLoadedState(restored);
                _status.Text = L("Wiederhergestellt aus ", "Restored from ") + sourceDescription +
                    L(". Sicherung erstellt: ", ". Backup created: ") + Path.GetFileName(backup);
                _status.ForeColor = Color.FromArgb(164, 205, 161);
                MessageBox.Show(this, _status.Text, L("Wiederherstellung abgeschlossen", "Restoration complete"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (temp != null && File.Exists(temp))
                {
                    try { File.Delete(temp); }
                    catch { }
                }
                if (replaced && backup != null && File.Exists(backup) && _loadedPath != null)
                {
                    try
                    {
                        FileAttributes currentAttributes = File.GetAttributes(_loadedPath);
                        File.SetAttributes(_loadedPath, currentAttributes & ~FileAttributes.ReadOnly);
                        File.Copy(backup, _loadedPath, true);
                        if (haveOriginalAttributes)
                            File.SetAttributes(_loadedPath, originalAttributes);
                    }
                    catch { }
                }
                else if (haveOriginalAttributes && _loadedPath != null && File.Exists(_loadedPath))
                {
                    try { File.SetAttributes(_loadedPath, originalAttributes); }
                    catch { }
                }
                ShowSaveError(ex);
            }
        }

        private string BuildRestorePreview(byte[] current, byte[] replacement, string sourceDescription)
        {
            int oldWidth = BitConverter.ToInt32(current, WidthOffset);
            int oldHeight = BitConverter.ToInt32(current, HeightOffset);
            int newWidth = BitConverter.ToInt32(replacement, WidthOffset);
            int newHeight = BitConverter.ToInt32(replacement, HeightOffset);
            bool oldWindowed = ReadWindowMode(current);
            bool newWindowed = ReadWindowMode(replacement);
            List<string> lines = new List<string>();
            lines.Add(L("Wiederherstellen: ", "Restore from: ") + sourceDescription);
            if (oldWidth != newWidth || oldHeight != newHeight)
                lines.Add(L("Auflösung: ", "Resolution: ") + oldWidth + " × " + oldHeight + " → " + newWidth + " × " + newHeight);
            if (oldWindowed != newWindowed)
                lines.Add(L("Anzeigemodus: ", "Display mode: ") + ModeName(oldWindowed) + " → " + ModeName(newWindowed));
            lines.Add(L("Weitere Fiesta-Einstellungen aus diesem Stand werden ebenfalls wiederhergestellt.",
                "Other Fiesta settings from this saved state will also be restored."));
            lines.Add("");
            lines.Add(L("Vorher wird eine Sicherung der aktuellen Datei erstellt. Fortfahren?",
                "A backup of the current file will be created first. Continue?"));
            return String.Join(Environment.NewLine, lines.ToArray());
        }

        private static bool ByteArraysEqual(byte[] first, byte[] second)
        {
            if (first == null || second == null || first.Length != second.Length)
                return false;
            for (int i = 0; i < first.Length; i++)
                if (first[i] != second[i])
                    return false;
            return true;
        }

        private void ValidateOptionData(byte[] data)
        {
            if (data.Length != SupportedLength)
                throw new InvalidDataException(L("Diese Sicherung gehört nicht zum unterstützten 68-Byte-Format.",
                    "This backup is not in the supported 68-byte format."));
            ValidateResolution(BitConverter.ToInt32(data, WidthOffset), BitConverter.ToInt32(data, HeightOffset));
            ReadWindowMode(data);
        }

        private void UpdateLoadedState(byte[] data)
        {
            _loadedWidth = BitConverter.ToInt32(data, WidthOffset);
            _loadedHeight = BitConverter.ToInt32(data, HeightOffset);
            _loadedWindowed = ReadWindowMode(data);
            _loadedReadOnly = (File.GetAttributes(_loadedPath) & FileAttributes.ReadOnly) != 0;
            _loadedFingerprint = ComputeFingerprint(data);
            _updatingResolution = true;
            _widthBox.Value = _loadedWidth;
            _heightBox.Value = _loadedHeight;
            _modeBox.SelectedIndex = _loadedWindowed ? 0 : 1;
            _readOnlyAfterSave.Checked = _loadedReadOnly;
            SelectMatchingPreset(_loadedWidth, _loadedHeight);
            _updatingResolution = false;
            _currentResolution.Text = _loadedWidth + " × " + _loadedHeight;
            RefreshFileState();
            _fileState.ForeColor = _loadedReadOnly ? Gold : Color.FromArgb(193, 172, 191);
            UpdateProtectionButtons();
        }

        private string GetDefaultConfigPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "FiestaResolutionEditor", "Defaults");
            string normalizedPath = Path.GetFullPath(_loadedPath).ToUpperInvariant();
            string key = ComputeFingerprint(Encoding.UTF8.GetBytes(normalizedPath))
                .Replace("/", "_").Replace("+", "-");
            return Path.Combine(folder, key + ".mco");
        }

        private static string ComputeFingerprint(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(data));
        }

        private void EnsureGameClosed()
        {
            Process[] processes = Process.GetProcessesByName("Fiesta");
            bool running = processes.Length > 0;
            foreach (Process process in processes)
                process.Dispose();
            if (running)
                throw new InvalidOperationException(L("Bitte Fiesta vollständig schließen, bevor du diese Datei änderst.",
                    "Close Fiesta completely before changing this file."));
        }

        private void EnsureFileHasNotChanged(byte[] currentData)
        {
            if (_loadedFingerprint == null ||
                !String.Equals(ComputeFingerprint(currentData), _loadedFingerprint, StringComparison.Ordinal))
                throw new InvalidDataException(L("Die Datei wurde seit dem Laden verändert. Lade sie erneut, bevor du fortfährst.",
                    "The file has changed since it was loaded. Reload it before continuing."));
        }

        private Panel BuildHeader()
        {
            Panel panel = new Panel();
            panel.Location = new Point(0, 0);
            panel.Size = new Size(850, 112);
            panel.Paint += delegate(object sender, PaintEventArgs e)
            {
                Rectangle r = panel.ClientRectangle;
                using (LinearGradientBrush brush = new LinearGradientBrush(r,
                    Color.FromArgb(52, 22, 68), Color.FromArgb(102, 31, 91), 0f))
                {
                    e.Graphics.FillRectangle(brush, r);
                }

                using (Pen line = new Pen(Color.FromArgb(166, 125, 82), 1f))
                {
                    e.Graphics.DrawLine(line, 0, r.Height - 2, r.Width, r.Height - 2);
                }

                using (SolidBrush spark = new SolidBrush(Color.FromArgb(82, 239, 204, 116)))
                {
                    e.Graphics.FillEllipse(spark, 684, 18, 4, 4);
                    e.Graphics.FillEllipse(spark, 729, 61, 3, 3);
                    e.Graphics.FillEllipse(spark, 796, 32, 3, 3);
                    e.Graphics.FillEllipse(spark, 640, 78, 2, 2);
                    e.Graphics.FillEllipse(spark, 752, 16, 2, 2);
                }
            };

            Panel emblem = new Panel();
            emblem.Location = new Point(25, 21);
            emblem.Size = new Size(70, 70);
            emblem.BackColor = Color.Transparent;
            emblem.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle outer = new Rectangle(4, 4, 60, 60);
                using (LinearGradientBrush fill = new LinearGradientBrush(outer,
                    Color.FromArgb(74, 38, 94), Color.FromArgb(36, 20, 48), 45f))
                using (Pen goldPen = new Pen(Gold, 2f))
                using (Pen innerPen = new Pen(Color.FromArgb(163, 116, 180), 1f))
                {
                    e.Graphics.FillEllipse(fill, outer);
                    e.Graphics.DrawEllipse(goldPen, outer);
                    e.Graphics.DrawEllipse(innerPen, 10, 10, 48, 48);
                }

                Point[] star = new Point[]
                {
                    new Point(34, 13), new Point(39, 29), new Point(56, 34),
                    new Point(39, 39), new Point(34, 56), new Point(29, 39),
                    new Point(12, 34), new Point(29, 29)
                };
                using (SolidBrush b = new SolidBrush(Color.FromArgb(250, 226, 155)))
                {
                    e.Graphics.FillPolygon(b, star);
                }
            };
            panel.Controls.Add(emblem);

            Label title = new Label();
            title.Text = "FIESTA ONLINE";
            title.Location = new Point(112, 22);
            title.Size = new Size(500, 37);
            title.Font = new Font("Georgia", 21f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(255, 224, 154);
            title.BackColor = Color.Transparent;
            panel.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "AUFLÖSUNGSWERKSTATT     •     OPTION.MCO";
            subtitle.Tag = subtitle.Text;
            subtitle.Location = new Point(115, 64);
            subtitle.Size = new Size(550, 22);
            subtitle.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            subtitle.ForeColor = Color.FromArgb(239, 213, 222);
            subtitle.BackColor = Color.Transparent;
            panel.Controls.Add(subtitle);

            _languageCaption = MakeLabel("SPRACHE", GoldSoft, 8f, true);
            _languageCaption.Location = new Point(696, 18);
            _languageCaption.Size = new Size(126, 16);
            panel.Controls.Add(_languageCaption);

            _languageBox = new ComboBox();
            _languageBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _languageBox.FlatStyle = FlatStyle.Flat;
            _languageBox.BackColor = CardLight;
            _languageBox.ForeColor = WhiteText;
            _languageBox.Font = new Font("Segoe UI", 9f);
            _languageBox.Location = new Point(696, 39);
            _languageBox.Size = new Size(126, 27);
            _languageBox.Items.Add("Deutsch");
            _languageBox.Items.Add("English");
            _languageBox.SelectedIndex = _english ? 1 : 0;
            panel.Controls.Add(_languageBox);

            return panel;
        }

        private Panel CreateCard(string heading, Rectangle bounds)
        {
            Panel panel = new Panel();
            panel.Location = bounds.Location;
            panel.Size = bounds.Size;
            panel.BackColor = Card;
            panel.Paint += delegate(object sender, PaintEventArgs e)
            {
                Rectangle r = panel.ClientRectangle;
                r.Width -= 1;
                r.Height -= 1;
                using (Pen border = new Pen(Color.FromArgb(103, 71, 114), 1f))
                {
                    e.Graphics.DrawRectangle(border, r);
                }
                using (Pen accent = new Pen(Color.FromArgb(175, 130, 84), 2f))
                {
                    e.Graphics.DrawLine(accent, 0, 0, Math.Min(42, panel.Width), 0);
                }
            };

            Label header = MakeLabel(heading, Gold, 9f, true);
            header.Location = new Point(20, 12);
            header.Size = new Size(bounds.Width - 40, 20);
            panel.Controls.Add(header);
            return panel;
        }

        private static Label MakeLabel(string text, Color color, float size, bool bold)
        {
            Label label = new Label();
            label.Text = text;
            label.Tag = text;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
            label.AutoEllipsis = true;
            return label;
        }

        private static void StyleTextBox(TextBox box)
        {
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = Color.FromArgb(27, 19, 37);
            box.ForeColor = Color.FromArgb(245, 233, 240);
            box.Font = new Font("Segoe UI", 9f);
        }

        private static NumericUpDown CreateNumber(int x, int y, int width, int max, int initial)
        {
            NumericUpDown number = new NumericUpDown();
            number.Location = new Point(x, y);
            number.Size = new Size(width, 29);
            number.Minimum = 640;
            number.Maximum = max;
            number.Value = initial;
            number.Increment = 1;
            number.BorderStyle = BorderStyle.FixedSingle;
            number.BackColor = Color.FromArgb(27, 19, 37);
            number.ForeColor = Color.FromArgb(255, 223, 153);
            number.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            return number;
        }

        private ThemeButton CreateButton(string text, int x, int y, int width, int height, bool primary)
        {
            ThemeButton button = primary
                ? new ThemeButton(text, Color.FromArgb(172, 62, 131), Color.FromArgb(104, 35, 97), Gold)
                : new ThemeButton(text, Color.FromArgb(87, 47, 105), Color.FromArgb(54, 29, 72), Color.FromArgb(125, 88, 136));
            button.Location = new Point(x, y);
            button.Size = new Size(width, height);
            button.Tag = text;
            return button;
        }

        private void AddPresets()
        {
            ResolutionPreset[] presets = new ResolutionPreset[]
            {
                new ResolutionPreset(640, 480, "640 × 480   ·   4:3"),
                new ResolutionPreset(800, 600, "800 × 600   ·   4:3"),
                new ResolutionPreset(1024, 768, "1024 × 768   ·   4:3"),
                new ResolutionPreset(1280, 720, "1280 × 720   ·   HD"),
                new ResolutionPreset(1280, 800, "1280 × 800   ·   16:10"),
                new ResolutionPreset(1366, 768, "1366 × 768   ·   HD+"),
                new ResolutionPreset(1440, 900, "1440 × 900   ·   16:10"),
                new ResolutionPreset(1600, 900, "1600 × 900   ·   16:9"),
                new ResolutionPreset(1680, 1050, "1680 × 1050   ·   16:10"),
                new ResolutionPreset(1920, 1080, "1920 × 1080   ·   Full HD"),
                new ResolutionPreset(1920, 1200, "1920 × 1200   ·   16:10"),
                new ResolutionPreset(2048, 1080, "2048 × 1080   ·   17:9"),
                new ResolutionPreset(2560, 1080, "2560 × 1080   ·   21:9"),
                new ResolutionPreset(2560, 1440, "2560 × 1440   ·   QHD"),
                new ResolutionPreset(2560, 1600, "2560 × 1600   ·   16:10"),
                new ResolutionPreset(3440, 1440, "3440 × 1440   ·   21:9"),
                new ResolutionPreset(3840, 1600, "3840 × 1600   ·   21:9"),
                new ResolutionPreset(3840, 2160, "3840 × 2160   ·   4K UHD"),
                new ResolutionPreset(5120, 1440, "5120 × 1440   ·   32:9 DQHD"),
                new ResolutionPreset(5120, 2160, "5120 × 2160   ·   21:9 5K")
            };

            foreach (ResolutionPreset preset in presets)
                _presetBox.Items.Add(preset);

            foreach (ResolutionPreset detected in GetDetectedDisplayModes())
                _presetBox.Items.Add(detected);

            _presetBox.Items.Add(ResolutionPreset.Custom());
            _presetBox.SelectedIndex = 9;
        }

        private List<ResolutionPreset> GetDetectedDisplayModes()
        {
            Dictionary<string, string> monitorsByResolution = new Dictionary<string, string>();
            Screen[] screens = Screen.AllScreens;
            for (int screenIndex = 0; screenIndex < screens.Length; screenIndex++)
            {
                string monitorLabel = "Monitor " + (screenIndex + 1);
                bool foundMode = false;
                int modeNumber = 0;
                try
                {
                    DevMode mode = CreateDevMode();
                    while (EnumDisplaySettings(screens[screenIndex].DeviceName, modeNumber++, ref mode))
                    {
                        if (mode.dmPelsWidth >= 640 && mode.dmPelsWidth <= 8192 &&
                            mode.dmPelsHeight >= 480 && mode.dmPelsHeight <= 4320)
                        {
                            foundMode = true;
                            AddMonitorResolution(monitorsByResolution, mode.dmPelsWidth, mode.dmPelsHeight, monitorLabel);
                        }
                        mode = CreateDevMode();
                    }
                }
                catch
                {
                    // Keep the editor usable if a display driver does not expose its mode list.
                }

                if (!foundMode)
                    AddMonitorResolution(monitorsByResolution, screens[screenIndex].Bounds.Width,
                        screens[screenIndex].Bounds.Height, monitorLabel);
            }

            List<ResolutionPreset> detected = new List<ResolutionPreset>();
            foreach (KeyValuePair<string, string> item in monitorsByResolution)
            {
                string[] size = item.Key.Split('x');
                int width;
                int height;
                if (Int32.TryParse(size[0], out width) && Int32.TryParse(size[1], out height))
                    detected.Add(new ResolutionPreset(width, height, width + " × " + height + "   ·   " + item.Value));
            }

            detected.Sort(delegate(ResolutionPreset a, ResolutionPreset b)
            {
                int byWidth = a.Width.CompareTo(b.Width);
                return byWidth != 0 ? byWidth : a.Height.CompareTo(b.Height);
            });
            return detected;
        }

        private static DevMode CreateDevMode()
        {
            DevMode mode = new DevMode();
            mode.dmDeviceName = new String('\0', 32);
            mode.dmFormName = new String('\0', 32);
            mode.dmSize = (short)Marshal.SizeOf(typeof(DevMode));
            return mode;
        }

        private static void AddMonitorResolution(Dictionary<string, string> monitorMap, int width, int height, string monitor)
        {
            string key = width + "x" + height;
            string existing;
            if (!monitorMap.TryGetValue(key, out existing))
                monitorMap[key] = monitor;
            else if (existing.IndexOf(monitor, StringComparison.OrdinalIgnoreCase) < 0)
                monitorMap[key] = existing + ", " + monitor;
        }

        private NumericUpDown CreateNumber(int x, int y, int width, int max, int initial, int min)
        {
            NumericUpDown number = CreateNumber(x, y, width, max, initial);
            number.Minimum = min;
            return number;
        }

        private void BrowseForFile(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = L("Fiesta Online – Option.mco auswählen", "Fiesta Online – Select Option.mco");
                dialog.Filter = L(
                    "Fiesta Option.mco|Option.mco|MCO-Dateien (*.mco)|*.mco|Alle Dateien (*.*)|*.*",
                    "Fiesta Option.mco|Option.mco|MCO files (*.mco)|*.mco|All files (*.*)|*.*");
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                string folder = Path.GetDirectoryName(_pathBox.Text);
                if (!String.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    dialog.InitialDirectory = folder;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _pathBox.Text = dialog.FileName;
                    LoadSelectedFile();
                }
            }
        }

        private void SelectGameFolder(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = L("Wähle den Fiesta-Installationsordner aus.", "Select the Fiesta installation folder.");
                dialog.ShowNewFolderButton = false;
                string initial = Path.GetDirectoryName(_pathBox.Text);
                for (int i = 0; i < 2 && !String.IsNullOrEmpty(initial); i++)
                    initial = Path.GetDirectoryName(initial);
                if (!String.IsNullOrEmpty(initial) && Directory.Exists(initial))
                    dialog.SelectedPath = initial;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    List<string> candidates = new List<string>(Directory.GetFiles(
                        dialog.SelectedPath, "Option.mco", SearchOption.AllDirectories));
                    if (candidates.Count == 0)
                    {
                        MessageBox.Show(this,
                            L("Im ausgewählten Ordner wurde keine Option.mco gefunden.",
                              "No Option.mco was found in the selected folder."),
                            L("Datei nicht gefunden", "File not found"),
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    string selected = ChooseOptionCandidate(candidates);
                    if (selected == null)
                        return;
                    _pathBox.Text = selected;
                    LoadSelectedFile();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        L("Der Ordner konnte nicht durchsucht werden: ", "Could not search the folder: ") + ex.Message,
                        L("Ordner nicht lesbar", "Folder could not be read"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private string ChooseOptionCandidate(List<string> candidates)
        {
            if (candidates.Count == 1)
                return candidates[0];

            using (Form picker = new Form())
            {
                picker.Text = L("Option.mco auswählen", "Select Option.mco");
                picker.StartPosition = FormStartPosition.CenterParent;
                picker.FormBorderStyle = FormBorderStyle.FixedDialog;
                picker.MaximizeBox = false;
                picker.MinimizeBox = false;
                picker.ClientSize = new Size(620, 170);
                picker.BackColor = Night;
                picker.ForeColor = WhiteText;
                picker.Font = Font;

                Label prompt = MakeLabel(L("Mehrere Dateien gefunden. Wähle die Datei für deinen Fiesta-Client.",
                    "Multiple files found. Choose the one for your Fiesta client."), GoldSoft, 9f, false);
                prompt.Location = new Point(20, 16);
                prompt.Size = new Size(580, 26);
                picker.Controls.Add(prompt);

                ComboBox choices = new ComboBox();
                choices.DropDownStyle = ComboBoxStyle.DropDownList;
                choices.FlatStyle = FlatStyle.Flat;
                choices.BackColor = CardLight;
                choices.ForeColor = WhiteText;
                choices.Font = new Font("Segoe UI", 9f);
                choices.Location = new Point(20, 48);
                choices.Size = new Size(580, 28);
                foreach (string candidate in candidates)
                    choices.Items.Add(candidate);
                choices.SelectedIndex = 0;
                picker.Controls.Add(choices);

                ThemeButton cancel = CreateButton(L("Abbrechen", "Cancel"), 320, 112, 130, 34, false);
                cancel.Click += delegate { picker.DialogResult = DialogResult.Cancel; picker.Close(); };
                picker.Controls.Add(cancel);

                ThemeButton useFile = CreateButton(L("Datei verwenden", "Use file"), 470, 112, 130, 34, true);
                useFile.Click += delegate { picker.DialogResult = DialogResult.OK; picker.Close(); };
                picker.Controls.Add(useFile);
                picker.CancelButton = cancel;

                return picker.ShowDialog(this) == DialogResult.OK
                    ? choices.SelectedItem as string
                    : null;
            }
        }

        private void TryLoadDefaultFile()
        {
            if (File.Exists(_pathBox.Text))
                LoadSelectedFile();
        }

        private void LoadSelectedFile()
        {
            try
            {
                string path = Path.GetFullPath(_pathBox.Text.Trim());
                if (!File.Exists(path))
                    throw new FileNotFoundException(L("Diese Option.mco-Datei wurde nicht gefunden.", "Option.mco was not found."), path);

                byte[] data = File.ReadAllBytes(path);
                if (data.Length != SupportedLength)
                    throw new InvalidDataException(L("Unbekanntes Dateiformat: ", "Unknown file format: ") + data.Length +
                        L(" Byte. Diese Version unterstützt sicher 68-Byte-Option.mco-Dateien und hat nichts geändert.",
                          " bytes. This version only supports 68-byte Option.mco files and made no changes."));

                if (!BitConverter.IsLittleEndian)
                    throw new InvalidDataException(L("Dieses Option.mco-Format wird auf diesem System nicht unterstützt.",
                        "This Option.mco format is not supported on this system."));

                int width = BitConverter.ToInt32(data, WidthOffset);
                int height = BitConverter.ToInt32(data, HeightOffset);
                ValidateResolution(width, height);
                bool windowed = ReadWindowMode(data);

                FileAttributes attributes = File.GetAttributes(path);
                _loadedPath = path;
                _loadedWidth = width;
                _loadedHeight = height;
                _loadedWindowed = windowed;
                _loadedReadOnly = (attributes & FileAttributes.ReadOnly) != 0;
                _loadedFingerprint = ComputeFingerprint(data);
                _pathBox.SelectionStart = _pathBox.Text.Length;
                _pathBox.SelectionLength = 0;

                _updatingResolution = true;
                _widthBox.Value = width;
                _heightBox.Value = height;
                _modeBox.SelectedIndex = windowed ? 0 : 1;
                _readOnlyAfterSave.Checked = _loadedReadOnly;
                SelectMatchingPreset(width, height);
                _updatingResolution = false;

                _currentResolution.Text = width + " × " + height;
                RefreshFileState();
                _fileState.ForeColor = _loadedReadOnly ? Gold : Color.FromArgb(193, 172, 191);
                _status.Text = L("Datei geladen. Auflösung und Anzeigemodus können geändert werden.",
                    "File loaded. Resolution and display mode can be changed.");
                _status.ForeColor = Color.FromArgb(164, 205, 161);
                _saveButton.Enabled = true;
                UpdateProtectionButtons();
            }
            catch (Exception ex)
            {
                _loadedPath = null;
                _loadedFingerprint = null;
                _saveButton.Enabled = false;
                _restoreButton.Enabled = false;
                _readOnlyButton.Enabled = false;
                _unlockButton.Enabled = false;
                _fileState.Text = L("Datei konnte nicht geladen werden", "Could not load the file");
                _fileState.ForeColor = Color.FromArgb(255, 142, 153);
                _status.Text = ex.Message;
                _status.ForeColor = Color.FromArgb(255, 142, 153);
                MessageBox.Show(this, ex.Message, L("Option.mco nicht geladen", "Option.mco not loaded"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SelectMatchingPreset(int width, int height)
        {
            for (int i = 0; i < _presetBox.Items.Count; i++)
            {
                ResolutionPreset preset = _presetBox.Items[i] as ResolutionPreset;
                if (preset != null && !preset.IsCustom &&
                    preset.Width == width && preset.Height == height)
                {
                    _presetBox.SelectedIndex = i;
                    return;
                }
            }
            _presetBox.SelectedIndex = _presetBox.Items.Count - 1;
        }

        private void PresetChanged(object sender, EventArgs e)
        {
            ResolutionPreset preset = _presetBox.SelectedItem as ResolutionPreset;
            if (preset == null || preset.IsCustom)
                return;

            _updatingResolution = true;
            _widthBox.Value = preset.Width;
            _heightBox.Value = preset.Height;
            _updatingResolution = false;
            UpdatePreviewStatus();
        }

        private void CustomResolutionChanged(object sender, EventArgs e)
        {
            if (_updatingResolution)
                return;

            _updatingResolution = true;
            bool found = false;
            for (int i = 0; i < _presetBox.Items.Count - 1; i++)
            {
                ResolutionPreset preset = _presetBox.Items[i] as ResolutionPreset;
                if (preset != null && preset.Width == (int)_widthBox.Value &&
                    preset.Height == (int)_heightBox.Value)
                {
                    _presetBox.SelectedIndex = i;
                    found = true;
                    break;
                }
            }
            if (!found)
                _presetBox.SelectedIndex = _presetBox.Items.Count - 1;
            _updatingResolution = false;
            UpdatePreviewStatus();
        }

        private void UpdatePreviewStatus()
        {
            if (_loadedPath == null)
                return;

            string selected = (int)_widthBox.Value + " × " + (int)_heightBox.Value;
            bool selectedWindowed = _modeBox.SelectedIndex == 0;
            if ((int)_widthBox.Value == _loadedWidth && (int)_heightBox.Value == _loadedHeight &&
                selectedWindowed == _loadedWindowed)
                _status.Text = L("Aktuelle Einstellungen ausgewählt: ", "Current settings selected: ") +
                    selected + " · " + ModeName(selectedWindowed) + ".";
            else
                _status.Text = L("Vorschau: ", "Preview: ") + _loadedWidth + " × " + _loadedHeight + " · " + ModeName(_loadedWindowed) +
                    "  →  " + selected + " · " + ModeName(selectedWindowed) + ".";
            _status.ForeColor = GoldSoft;
        }

        private void UpdateProtectionButtons()
        {
            bool available = _loadedPath != null;
            _restoreButton.Enabled = available;
            _readOnlyButton.Enabled = available && !_loadedReadOnly;
            _unlockButton.Enabled = available && _loadedReadOnly;
        }

        private void SetReadOnly(bool enabled)
        {
            try
            {
                if (_loadedPath == null)
                    throw new InvalidOperationException(L("Lade zuerst eine gültige Option.mco-Datei.", "Load a valid Option.mco file first."));

                FileAttributes attributes = File.GetAttributes(_loadedPath);
                bool current = (attributes & FileAttributes.ReadOnly) != 0;
                if (current != enabled)
                {
                    if (enabled)
                        attributes |= FileAttributes.ReadOnly;
                    else
                        attributes &= ~FileAttributes.ReadOnly;
                    File.SetAttributes(_loadedPath, attributes);
                }

                _loadedReadOnly = enabled;
                _readOnlyAfterSave.Checked = enabled;
                RefreshFileState();
                _fileState.ForeColor = enabled ? Gold : Color.FromArgb(193, 172, 191);
                _status.Text = enabled
                    ? L("Schreibschutz aktiviert.", "Read-only enabled.")
                    : L("Schreibschutz entfernt.", "Read-only disabled.");
                _status.ForeColor = Color.FromArgb(164, 205, 161);
                UpdateProtectionButtons();
            }
            catch (Exception ex)
            {
                ShowSaveError(ex);
            }
        }

        private void SaveResolution()
        {
            string backup = null;
            string temp = null;
            bool replaced = false;
            FileAttributes originalAttributes = 0;
            bool haveOriginalAttributes = false;

            try
            {
                if (_loadedPath == null)
                    throw new InvalidOperationException(L("Lade zuerst eine gültige Option.mco-Datei.", "Load a valid Option.mco file first."));

                EnsureGameClosed();

                string path = Path.GetFullPath(_pathBox.Text.Trim());
                if (!String.Equals(path, _loadedPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(L("Der Dateipfad hat sich geändert. Lade die neue Datei zuerst.",
                        "The file path changed. Load the new file first."));

                byte[] data = File.ReadAllBytes(path);
                if (data.Length != SupportedLength)
                    throw new InvalidDataException(L("Die Datei hat sich verändert oder gehört zu einer anderen Version. Es wurde nichts gespeichert.",
                        "The file changed or belongs to a different version. Nothing was saved."));
                EnsureFileHasNotChanged(data);

                int oldWidth = BitConverter.ToInt32(data, WidthOffset);
                int oldHeight = BitConverter.ToInt32(data, HeightOffset);
                bool oldWindowed = ReadWindowMode(data);
                if (oldWidth != _loadedWidth || oldHeight != _loadedHeight || oldWindowed != _loadedWindowed)
                    throw new InvalidDataException(L("Auflösung oder Anzeigemodus der Datei haben sich seit dem Laden verändert. Lade sie erneut.",
                        "The file's resolution or display mode changed after loading. Load it again."));

                int newWidth = Decimal.ToInt32(_widthBox.Value);
                int newHeight = Decimal.ToInt32(_heightBox.Value);
                bool newWindowed = GetSelectedWindowMode();
                ValidateResolution(newWidth, newHeight);

                originalAttributes = File.GetAttributes(path);
                haveOriginalAttributes = true;
                bool currentlyReadOnly = (originalAttributes & FileAttributes.ReadOnly) != 0;
                bool resolutionChanged = oldWidth != newWidth || oldHeight != newHeight;
                bool modeChanged = oldWindowed != newWindowed;
                bool settingsChanged = resolutionChanged || modeChanged;
                bool protectionChanged = currentlyReadOnly != _readOnlyAfterSave.Checked;

                if (!settingsChanged && !protectionChanged)
                {
                    _status.Text = L("Keine Änderung erforderlich.", "No changes needed.");
                    _status.ForeColor = Color.FromArgb(164, 205, 161);
                    return;
                }

                string preview = BuildChangePreview(oldWidth, oldHeight, newWidth, newHeight,
                    oldWindowed, newWindowed, currentlyReadOnly, _readOnlyAfterSave.Checked);
                if (MessageBox.Show(this, preview, L("Änderungen prüfen", "Review changes"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;

                backup = path + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
                File.Copy(path, backup, false);

                if (settingsChanged)
                {
                    byte[] w = BitConverter.GetBytes(newWidth);
                    byte[] h = BitConverter.GetBytes(newHeight);
                    Buffer.BlockCopy(w, 0, data, WidthOffset, 4);
                    Buffer.BlockCopy(h, 0, data, HeightOffset, 4);
                    data[WindowModeOffset] = newWindowed ? (byte)0 : (byte)1;
                    File.WriteAllBytes(temp, data);

                    if (currentlyReadOnly)
                        File.SetAttributes(path, originalAttributes & ~FileAttributes.ReadOnly);

                    File.Replace(temp, path, null);
                    temp = null;
                    replaced = true;
                }

                FileAttributes afterSave = originalAttributes;
                if (_readOnlyAfterSave.Checked)
                    afterSave |= FileAttributes.ReadOnly;
                else
                    afterSave &= ~FileAttributes.ReadOnly;
                File.SetAttributes(path, afterSave);

                byte[] saved = File.ReadAllBytes(path);
                int savedWidth = BitConverter.ToInt32(saved, WidthOffset);
                int savedHeight = BitConverter.ToInt32(saved, HeightOffset);
                bool savedWindowed = ReadWindowMode(saved);
                if (savedWidth != newWidth || savedHeight != newHeight || savedWindowed != newWindowed)
                    throw new IOException(L("Die Prüfung nach dem Speichern ist fehlgeschlagen.", "The post-save check failed."));

                _loadedWidth = savedWidth;
                _loadedHeight = savedHeight;
                _loadedWindowed = savedWindowed;
                _loadedReadOnly = _readOnlyAfterSave.Checked;
                _loadedFingerprint = ComputeFingerprint(saved);
                _currentResolution.Text = savedWidth + " × " + savedHeight;
                RefreshFileState();
                _fileState.ForeColor = _loadedReadOnly ? Gold : Color.FromArgb(193, 172, 191);
                _status.Text = L("Gespeichert: ", "Saved: ") + savedWidth + " × " + savedHeight + " · " + ModeName(savedWindowed) +
                    ". " + L("Sicherung: ", "Backup: ") + Path.GetFileName(backup);
                _status.ForeColor = Color.FromArgb(164, 205, 161);
                UpdateProtectionButtons();

                MessageBox.Show(this,
                    L("Einstellungen gespeichert: ", "Settings saved: ") + savedWidth + " × " + savedHeight + " · " + ModeName(savedWindowed) +
                    Environment.NewLine + Environment.NewLine +
                    L("Sicherung erstellt:", "Backup created:") + Environment.NewLine + backup +
                    Environment.NewLine + Environment.NewLine +
                    L("Fiesta kann die neue Auflösung beim nächsten Start laden.", "Fiesta can load the new resolution the next time it starts."),
                    L("Einstellungen gespeichert", "Settings saved"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (temp != null && File.Exists(temp))
                {
                    try { File.Delete(temp); }
                    catch { }
                }

                if (replaced && backup != null && File.Exists(backup) && _loadedPath != null)
                {
                    try
                    {
                        FileAttributes current = File.GetAttributes(_loadedPath);
                        File.SetAttributes(_loadedPath, current & ~FileAttributes.ReadOnly);
                        File.Copy(backup, _loadedPath, true);
                        if (haveOriginalAttributes)
                            File.SetAttributes(_loadedPath, originalAttributes);
                    }
                    catch { }
                }
                else if (haveOriginalAttributes && _loadedPath != null && File.Exists(_loadedPath))
                {
                    try { File.SetAttributes(_loadedPath, originalAttributes); }
                    catch { }
                }

                ShowSaveError(ex);
            }
        }

        private string BuildChangePreview(int oldWidth, int oldHeight, int newWidth, int newHeight,
            bool oldWindowed, bool newWindowed, bool currentlyReadOnly, bool readOnlyAfterSave)
        {
            List<string> lines = new List<string>();
            if (oldWidth != newWidth || oldHeight != newHeight)
                lines.Add(L("Auflösung: ", "Resolution: ") + oldWidth + " × " + oldHeight + " → " + newWidth + " × " + newHeight);
            if (oldWindowed != newWindowed)
                lines.Add(L("Anzeigemodus: ", "Display mode: ") + ModeName(oldWindowed) + " → " + ModeName(newWindowed));
            if (currentlyReadOnly != readOnlyAfterSave)
                lines.Add(L("Schreibschutz: ", "Read-only: ") +
                    (currentlyReadOnly ? L("Ein", "On") : L("Aus", "Off")) + " → " +
                    (readOnlyAfterSave ? L("Ein", "On") : L("Aus", "Off")));
            lines.Add("");
            lines.Add(L("Vor jeder Änderung wird eine Sicherung erstellt. Fortfahren?",
                "A backup will be created before changes. Continue?"));
            return String.Join(Environment.NewLine, lines.ToArray());
        }

        private void ValidateResolution(int width, int height)
        {
            if (width < 640 || width > 8192 || height < 480 || height > 4320)
                throw new ArgumentOutOfRangeException("resolution",
                    L("Erlaubt sind Breiten von 640 bis 8192 und Höhen von 480 bis 4320 Pixeln.",
                      "Allowed sizes are widths from 640 to 8192 and heights from 480 to 4320 pixels."));
        }

        private bool ReadWindowMode(byte[] data)
        {
            byte value = data[WindowModeOffset];
            if (value != 0 && value != 1)
                throw new InvalidDataException(L("Unbekannter Wert für den Fenstermodus in Option.mco. Es wurde nichts geändert.",
                    "Unknown display-mode value in Option.mco. No changes were made."));
            return value == 0;
        }

        private bool GetSelectedWindowMode()
        {
            if (_modeBox.SelectedIndex != 0 && _modeBox.SelectedIndex != 1)
                throw new InvalidOperationException(L("Wähle zuerst Fenster- oder Vollbildmodus aus.",
                    "Select windowed or fullscreen mode first."));
            return _modeBox.SelectedIndex == 0;
        }

        private string ModeName(bool windowed)
        {
            return windowed ? L("Fenstermodus", "Windowed") : L("Vollbildmodus", "Fullscreen");
        }

        private void ShowSaveError(Exception ex)
        {
            string message = ex.Message;
            if (ex is UnauthorizedAccessException)
                message = L("Windows verweigert den Schreibzugriff. Starte FiestaResolutionEditor.exe per Rechtsklick mit „Als Administrator ausführen“.",
                    "Windows denied write access. Right-click FiestaResolutionEditor.exe and choose Run as administrator.") +
                    Environment.NewLine + Environment.NewLine + message;

            _status.Text = message;
            _status.ForeColor = Color.FromArgb(255, 142, 153);
            MessageBox.Show(this, message, L("Änderung nicht möglich", "Could not make the change"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void PaintBadge(object sender, PaintEventArgs e)
        {
            Rectangle r = ((Control)sender).ClientRectangle;
            r.Width -= 1;
            r.Height -= 1;
            using (Pen p = new Pen(Color.FromArgb(133, 91, 137), 1f))
                e.Graphics.DrawRectangle(p, r);
            using (Pen p = new Pen(Color.FromArgb(176, 130, 84), 2f))
                e.Graphics.DrawLine(p, 0, 0, 46, 0);
        }
    }
}
