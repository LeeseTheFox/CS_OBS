using CS_OBS3;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace CS_OBS
{
    public class Program
    {
        static string obsWorkingDir = string.Empty;
        static int gameCheckInterval;
        static int obsCheckInterval;
        static readonly List<string> gameProcessNames = new();
        static bool addToStartup;
        static bool isObsRunning = false;
        static NotifyIcon? trayIcon;
        static CancellationTokenSource? cts;
        static SettingsForm? settingsForm;
        static PauseManager pauseManager = new PauseManager();

        public static bool AddToStartup { get => addToStartup; set => addToStartup = value; }

        [STAThread]
        static void Main()
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CS_OBS_log.txt");
            try
            {
                File.AppendAllText(logPath, $"[{DateTime.Now}] Application starting...\n");
                File.AppendAllText(logPath, $"[{DateTime.Now}] Current directory: {Environment.CurrentDirectory}\n");
                File.AppendAllText(logPath, $"[{DateTime.Now}] Executable path: {Application.ExecutablePath}\n");
                File.AppendAllText(logPath, $"[{DateTime.Now}] Is running as administrator: {IsUserAdministrator()}\n");

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                if (!IsUserAdministrator())
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now}] Attempting to restart with administrator privileges...\n");
                    RestartAsAdministrator();
                    return;
                }

                File.AppendAllText(logPath, $"[{DateTime.Now}] Reading config file...\n");
                ReadConfigFile();

                File.AppendAllText(logPath, $"[{DateTime.Now}] Initializing tray icon...\n");
                InitializeTrayIcon();

                File.AppendAllText(logPath, $"[{DateTime.Now}] Starting process monitor...\n");
                cts = new CancellationTokenSource();
                Task.Run(() => MonitorProcessesAsync(cts.Token), cts.Token);

                File.AppendAllText(logPath, $"[{DateTime.Now}] Entering message loop...\n");
                Application.Run();
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, $"[{DateTime.Now}] Error during startup: {ex}\n");
                MessageBox.Show($"An error occurred during startup. Please check the error log at {logPath}", "CS_OBS Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static void PauseMonitoring(TimeSpan? duration)
        {
            if (duration.HasValue)
            {
                pauseManager.Pause(duration.Value);
                trayIcon.ShowBalloonTip(3000, "CS_OBS Paused", $"Monitoring paused for {duration.Value.TotalMinutes} minutes", ToolTipIcon.Info);
            }
            else
            {
                pauseManager.PauseIndefinitely();
                trayIcon.ShowBalloonTip(3000, "CS_OBS Paused", "Monitoring paused until unpaused", ToolTipIcon.Info);
            }
        }

        static void UnpauseMonitoring()
        {
            pauseManager.Unpause();
            trayIcon.ShowBalloonTip(3000, "CS_OBS Unpaused", "Monitoring resumed", ToolTipIcon.Info);
        }

        private static void RestartAsAdministrator()
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CS_OBS_log.txt");
            try
            {
                ProcessStartInfo startInfo = new(Application.ExecutablePath)
                {
                    UseShellExecute = true,
                    Verb = "runas" // Request elevation
                };
                Process.Start(startInfo);
                File.AppendAllText(logPath, $"[{DateTime.Now}] Successfully requested elevation.\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, $"[{DateTime.Now}] Failed to restart with elevation: {ex}\n");
                MessageBox.Show("CS_OBS requires administrator privileges to run. Please restart the application as an administrator.", "Elevation Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        static bool IsProcessRunning(string processName)
        {
            return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName)).Length > 0;
        }

        static void CloseObsProcess()
        {
            foreach (var process in Process.GetProcessesByName("obs64"))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000); // Wait up to 5 seconds for the process to exit
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to close OBS: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        static void UpdateIntervals(int gameInterval, int obsInterval)
        {
            gameCheckInterval = gameInterval;
            obsCheckInterval = obsInterval;
        }

        static void InitializeTrayIcon()
        {
            trayIcon = new NotifyIcon()
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                Visible = true,
                Text = "CS_OBS"
            };

            ContextMenuStrip contextMenu = new ContextMenuStrip();

            ToolStripMenuItem settingsItem = new ToolStripMenuItem("Settings");
            settingsItem.Click += OnSettings;
            contextMenu.Items.Add(settingsItem);

            ToolStripMenuItem pauseMenu = new ToolStripMenuItem("Pause");
            pauseMenu.DropDownItems.Add("15 seconds", null, (s, e) => PauseMonitoring(TimeSpan.FromSeconds(15)));
            pauseMenu.DropDownItems.Add("1 minute", null, (s, e) => PauseMonitoring(TimeSpan.FromMinutes(1)));
            pauseMenu.DropDownItems.Add("1 hour", null, (s, e) => PauseMonitoring(TimeSpan.FromHours(1)));
            pauseMenu.DropDownItems.Add("6 hours", null, (s, e) => PauseMonitoring(TimeSpan.FromHours(6)));
            pauseMenu.DropDownItems.Add("Until unpaused", null, (s, e) => PauseMonitoring(null));
            contextMenu.Items.Add(pauseMenu);

            ToolStripMenuItem unpauseItem = new ToolStripMenuItem("Unpause");
            unpauseItem.Click += (s, e) => UnpauseMonitoring();
            contextMenu.Items.Add(unpauseItem);

            ToolStripMenuItem closeItem = new ToolStripMenuItem("Close");
            closeItem.Click += OnExit;
            contextMenu.Items.Add(closeItem);

            trayIcon.ContextMenuStrip = contextMenu;
        }

        static void OnSettings(object? sender, EventArgs e)
        {
            if (settingsForm == null || settingsForm.IsDisposed)
            {
                settingsForm = new SettingsForm();
                settingsForm.IntervalUpdated += UpdateIntervals;
                settingsForm.ConfigUpdated += ReadConfigFile;
                settingsForm.FormClosed += (s, args) =>
                {
                    settingsForm.Dispose();
                    settingsForm = null;
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                };
            }

            if (!settingsForm.Visible)
            {
                settingsForm.Show();
            }
            else
            {
                settingsForm.BringToFront();
            }
        }

        static void OnExit(object? sender, EventArgs e)
        {
            cts?.Cancel();

            if (isObsRunning)
            {
                int obsPID = GetProcessID("obs64.exe");
                if (obsPID != -1)
                {
                    CloseProcess(obsPID);
                }
            }

            settingsForm?.Close();
            settingsForm?.Dispose();

            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }

            Application.Exit();
        }

        static async Task MonitorProcessesAsync(CancellationToken token)
        {
            _ = new HashSet<string>();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!pauseManager.IsPaused)
                    {
                        bool isAnyGameRunning = false;
                        HashSet<string> currentlyRunningGames = new();

                        foreach (var processName in gameProcessNames)
                        {
                            if (IsProcessRunning(processName))
                            {
                                isAnyGameRunning = true;
                                currentlyRunningGames.Add(processName);
                            }
                        }

                        if (isAnyGameRunning && !isObsRunning)
                        {
                            if (!string.IsNullOrEmpty(obsWorkingDir))
                            {
                                string obsExePath = Path.Combine(obsWorkingDir, "obs64.exe");
                                string launchOptions = "--startreplaybuffer --disable-shutdown-check";
                                LaunchProcess(obsExePath, launchOptions, obsWorkingDir);
                                isObsRunning = true;
                            }
                        }
                        else if (!isAnyGameRunning && isObsRunning)
                        {
                            CloseObsProcess();
                            isObsRunning = false;
                        }

                        HashSet<string> runningGames = currentlyRunningGames;
                    }

                    int sleepInterval = isObsRunning ? obsCheckInterval : gameCheckInterval;
                    await Task.Delay(sleepInterval, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An error occurred: {ex.Message}");
                }
            }
        }

        static void ReadConfigFile()
        {
            string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");

            gameProcessNames.Clear();
            foreach (string line in File.ReadAllLines(configFilePath))
            {
                string[] parts = line.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    string key = parts[0].Trim();
                    string value = parts[1].Trim();

                    switch (key)
                    {
                        case "GAME_PROCESS_INTERVAL":
                            gameCheckInterval = int.Parse(value);
                            break;
                        case "OBS_PROCESS_INTERVAL":
                            obsCheckInterval = int.Parse(value);
                            break;
                        case "OBS_WORKING_DIR":
                            obsWorkingDir = value ?? string.Empty;
                            break;
                        case "ADD_TO_STARTUP":
                            AddToStartup = value == "1";
                            break;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                {
                    gameProcessNames.Add(line.Trim());
                }
            }
        }

        static int GetProcessID(string processName)
        {
            var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName));
            try
            {
                return processes.Length > 0 ? processes[0].Id : -1;
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        static void CloseProcess(int processID)
        {
            try
            {
                var process = Process.GetProcessById(processID);
                try
                {
                    process.Kill();
                }
                finally
                {
                    process.Dispose();
                }
            }
            catch (Exception) { }
        }

        static void LaunchProcess(string path, string arguments, string workingDir)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = path,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(startInfo);
        }

        public static bool IsUserAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public class SettingsForm : Form
    {
        private CheckBox startOnBootCheckBox = null!;
        private TextBox gameIntervalTextBox = null!;
        private TextBox obsIntervalTextBox = null!;
        private Button applyButton = null!;
        private Button gameDefaultButton = null!;
        private Button obsDefaultButton = null!;
        private TextBox obsPathTextBox = null!;
        private Button browseButton = null!;
        private ListBox gameListBox = null!;
        private Button addGameButton = null!;
        private Button removeGameButton = null!;

        private const int DefaultGameInterval = 15000;
        private const int DefaultObsInterval = 5000;
        private const int MinInterval = 1;
        private const int WarningThreshold = 50;

        public event Action<int, int>? IntervalUpdated;
        public event Action? ConfigUpdated;

        public SettingsForm()
        {
            InitializeComponents();
            LoadSettings();
        }

        private void InitializeComponents()
        {
            this.Text = "CS_OBS settings";
            this.Size = new Size(455, 550);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = ColorTranslator.FromHtml("#1E1E1E");
            this.ForeColor = Color.White;

            int margin = 30;
            int controlWidth = 380;
            int controlX = margin;
            int textBoxHeight = 25;
            int buttonWidth = 80;

            gameIntervalTextBox = new TextBox
            {
                Location = new Point(controlX, 50),
                Size = new Size(controlWidth - buttonWidth - 5, textBoxHeight),
                BackColor = ColorTranslator.FromHtml("#2D2D2D"),
                ForeColor = Color.White
            };

            obsIntervalTextBox = new TextBox
            {
                Location = new Point(controlX, 120),
                Size = new Size(controlWidth - buttonWidth - 5, textBoxHeight),
                BackColor = ColorTranslator.FromHtml("#2D2D2D"),
                ForeColor = Color.White
            };

            gameDefaultButton = new Button
            {
                Text = "Default",
                Location = new Point(controlX + controlWidth - buttonWidth, 49),
                Size = new Size(buttonWidth, textBoxHeight),
                BackColor = ColorTranslator.FromHtml("#3D3D3D"),
                ForeColor = Color.White
            };

            obsDefaultButton = new Button
            {
                Text = "Default",
                Location = new Point(controlX + controlWidth - buttonWidth, 119),
                Size = new Size(buttonWidth, textBoxHeight),
                BackColor = ColorTranslator.FromHtml("#3D3D3D"),
                ForeColor = Color.White
            };

            obsPathTextBox = new TextBox
            {
                Location = new Point(controlX, 190),
                Size = new Size(controlWidth - buttonWidth - 5, textBoxHeight),
                BackColor = ColorTranslator.FromHtml("#2D2D2D"),
                ForeColor = Color.White,
                ReadOnly = true
            };

            browseButton = new Button
            {
                Text = "Browse",
                Location = new Point(controlX + controlWidth - buttonWidth, 189),
                Size = new Size(buttonWidth, textBoxHeight),
                BackColor = ColorTranslator.FromHtml("#3D3D3D"),
                ForeColor = Color.White
            };

            startOnBootCheckBox = new CheckBox
            {
                Text = "Start CS_OBS automatically on system boot",
                AutoSize = true,
                Location = new Point(controlX, 230),
                ForeColor = Color.White
            };

            this.Controls.Add(startOnBootCheckBox);

            gameListBox = new ListBox
            {
                Location = new Point(controlX, 300),
                Size = new Size(controlWidth, 100),
                BackColor = ColorTranslator.FromHtml("#2D2D2D"),
                ForeColor = Color.White
            };

            addGameButton = new Button
            {
                Text = "Add game",
                Location = new Point(controlX - 1, 410),
                Size = new Size(100, 30),
                BackColor = ColorTranslator.FromHtml("#3D3D3D"),
                ForeColor = Color.White
            };

            removeGameButton = new Button
            {
                Text = "Remove game",
                Location = new Point(controlX + 110, 410),
                Size = new Size(100, 30),
                BackColor = ColorTranslator.FromHtml("#3D3D3D"),
                ForeColor = Color.White,
                Visible = false
            };

            applyButton = new Button
            {
                Text = "Apply",
                Location = new Point(controlX + controlWidth - 99, 470),
                Size = new Size(100, 30),
                BackColor = ColorTranslator.FromHtml("#3D3D3D"),
                ForeColor = Color.White
            };

            gameIntervalTextBox.KeyPress += TextBox_KeyPress;
            obsIntervalTextBox.KeyPress += TextBox_KeyPress;
            gameDefaultButton.Click += (sender, e) => gameIntervalTextBox.Text = DefaultGameInterval.ToString();
            obsDefaultButton.Click += (sender, e) => obsIntervalTextBox.Text = DefaultObsInterval.ToString();
            browseButton.Click += BrowseButton_Click;
            addGameButton.Click += AddGameButton_Click;
            removeGameButton.Click += RemoveGameButton_Click;
            applyButton.Click += ApplyButton_Click;
            gameListBox.SelectedIndexChanged += GameListBox_SelectedIndexChanged;

            this.Controls.AddRange(new Control[] {
                    gameIntervalTextBox, obsIntervalTextBox, applyButton,
                    gameDefaultButton, obsDefaultButton, obsPathTextBox,
                    browseButton, startOnBootCheckBox, gameListBox,
                    addGameButton, removeGameButton
                });

            AddLabel("Delay before opening OBS after you have opened the game (ms):", controlX - 3, 30);
            AddLabel("Delay before closing OBS after you have closed the game (ms):", controlX - 3, 100);
            AddLabel("OBS executable path:", controlX - 3, 170);
            AddLabel("Added games:", controlX - 3, 280);
        }

        private void AddLabel(string text, int x, int y)
        {
            Label label = new()
            {
                Text = text,
                AutoSize = true,
                Location = new Point(x, y),
                ForeColor = Color.White
            };
            this.Controls.Add(label);
        }

        private static void TextBox_KeyPress(object? sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        }

        private void BrowseButton_Click(object? sender, EventArgs e)
        {
            using OpenFileDialog openFileDialog = new();
            openFileDialog.Filter = "OBS Executable|obs64.exe";
            openFileDialog.Title = "Select obs64.exe";

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string selectedFile = openFileDialog.FileName;
                if (Path.GetFileName(selectedFile).ToLower() == "obs64.exe")
                {
                    obsPathTextBox.Text = selectedFile;
                }
                else
                {
                    MessageBox.Show("Please select the obs64.exe file.", "Invalid File", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void AddGameButton_Click(object? sender, EventArgs e)
        {
            using ProcessPickerForm processPicker = new();
            if (processPicker.ShowDialog() == DialogResult.OK)
            {
                string selectedProcess = processPicker.SelectedProcess;
                if (!string.IsNullOrEmpty(selectedProcess))
                {
                    AddGameToList(selectedProcess);
                }
            }
        }

        private void AddGameToList(string gameName)
        {
            string lowercaseGameName = gameName.ToLower();
            if (!gameListBox.Items.Cast<string>().Any(item => item.ToLower() == lowercaseGameName))
            {
                gameListBox.Items.Add(gameName);
            }
            else
            {
                MessageBox.Show($"The game '{gameName}' is already in the list.", "Duplicate entry", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void LoadSettings()
        {
            string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
            if (File.Exists(configFilePath))
            {
                string[] lines = File.ReadAllLines(configFilePath);
                foreach (string line in lines)
                {
                    if (line.StartsWith("GAME_PROCESS_INTERVAL="))
                    {
                        gameIntervalTextBox.Text = line.Split('=')[1];
                    }
                    else if (line.StartsWith("OBS_PROCESS_INTERVAL="))
                    {
                        obsIntervalTextBox.Text = line.Split('=')[1];
                    }
                    else if (line.StartsWith("OBS_WORKING_DIR="))
                    {
                        string obsDir = line.Split('=')[1];
                        if (!string.IsNullOrEmpty(obsDir))
                        {
                            obsPathTextBox.Text = Path.Combine(obsDir, "obs64.exe");
                        }
                    }
                    else if (line.StartsWith("ADD_TO_STARTUP="))
                    {
                        startOnBootCheckBox.Checked = line.Split('=')[1] == "1";
                    }
                    else if (!line.StartsWith("#") && line.EndsWith(".exe"))
                    {
                        gameListBox.Items.Add(line.Trim());
                    }
                }
            }
        }

        private void RemoveGameButton_Click(object? sender, EventArgs e)
        {
            if (gameListBox.SelectedItem != null)
            {
                gameListBox.Items.RemoveAt(gameListBox.SelectedIndex);
            }
        }

        private void GameListBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            removeGameButton.Visible = gameListBox.SelectedItem != null;
        }

        private void ApplyButton_Click(object? sender, EventArgs e)
        {
            if (ValidateAndParseInterval(gameIntervalTextBox.Text, out int gameInterval) &&
                ValidateAndParseInterval(obsIntervalTextBox.Text, out int obsInterval))
            {
                if (gameInterval <= WarningThreshold || obsInterval <= WarningThreshold)
                {
                    DialogResult result = MessageBox.Show(
                        "Setting an interval below 50ms may cause high CPU usage. Are you sure you want to continue?",
                        "Warning",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning
                    );

                    if (result == DialogResult.No)
                    {
                        return;
                    }
                }

                if (string.IsNullOrEmpty(obsPathTextBox.Text))
                {
                    MessageBox.Show("Please select the OBS executable (obs64.exe) file.", "Missing OBS Path", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                SaveSettings(gameInterval, obsInterval, Path.GetDirectoryName(obsPathTextBox.Text) ?? string.Empty);
                MessageBox.Show("Settings applied successfully.", "Settings Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Close();
            }
        }

        private static bool ValidateAndParseInterval(string input, out int result)
        {
            if (int.TryParse(input, out result) && result >= MinInterval)
            {
                return true;
            }
            else
            {
                MessageBox.Show($"Please enter a valid integer greater than or equal to {MinInterval}.", "Invalid Input", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void SaveSettings(int gameInterval, int obsInterval, string obsWorkingDir)
        {
            string configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
            string[] lines = File.ReadAllLines(configFilePath).ToList().Where(line => !line.EndsWith(".exe")).ToArray();

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("GAME_PROCESS_INTERVAL="))
                {
                    lines[i] = $"GAME_PROCESS_INTERVAL={gameInterval}";
                }
                else if (lines[i].StartsWith("OBS_PROCESS_INTERVAL="))
                {
                    lines[i] = $"OBS_PROCESS_INTERVAL={obsInterval}";
                }
                else if (lines[i].StartsWith("OBS_WORKING_DIR="))
                {
                    lines[i] = $"OBS_WORKING_DIR={obsWorkingDir}";
                }
                else if (lines[i].StartsWith("ADD_TO_STARTUP="))
                {
                    lines[i] = $"ADD_TO_STARTUP={(startOnBootCheckBox.Checked ? "1" : "0")}";
                }
            }

            List<string> configFileContent = lines.ToList();
            configFileContent.AddRange(gameListBox.Items.Cast<string>());

            File.WriteAllLines(configFilePath, configFileContent.ToArray());
            IntervalUpdated?.Invoke(gameInterval, obsInterval);
            ConfigUpdated?.Invoke();

            if (startOnBootCheckBox.Checked)
            {
                TaskSchedulerUtil.CreateStartupTask(Application.ExecutablePath);
            }
            else
            {
                TaskSchedulerUtil.DeleteStartupTask();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Dispose managed resources
                gameIntervalTextBox?.Dispose();
                obsIntervalTextBox?.Dispose();
                gameDefaultButton?.Dispose();
                obsDefaultButton?.Dispose();
                obsPathTextBox?.Dispose();
                browseButton?.Dispose();
                startOnBootCheckBox?.Dispose();
                gameListBox?.Dispose();
                addGameButton?.Dispose();
                removeGameButton?.Dispose();
                applyButton?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public class ProcessPickerForm : Form
    {
        private ListView processListView = null!;
        private Button selectButton = null!;
        private Button manualGameButton = null!;
        private System.Windows.Forms.Timer updateTimer = null!;
        public string? SelectedProcess { get; private set; }
        private ImageList iconImageList = null!;
        private readonly Dictionary<string, int> iconCache = new();
        private bool isAdjustingColumns = false;

        public ProcessPickerForm()
        {
            InitializeComponents();
            LoadProcesses();
        }

        private void InitializeComponents()
        {
            this.Text = "Select a process";
            this.Size = new Size(590, 500);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = ColorTranslator.FromHtml("#1E1E1E");
            this.ForeColor = Color.White;

            int margin = 30;

            iconImageList = new ImageList
            {
                ImageSize = new Size(16, 16),
                ColorDepth = ColorDepth.Depth32Bit
            };
            iconImageList.Images.Add("default", SystemIcons.Application);

            processListView = new ListView
        {
            Location = new Point(margin - 16, 10),
            Size = new Size(590 - 2 * margin + 16, 380),
            View = View.Details,
            FullRowSelect = true,
            BackColor = ColorTranslator.FromHtml("#2D2D2D"),
            ForeColor = Color.White,
            SmallImageList = iconImageList
        };
        processListView.Columns.Add("", 22);
        processListView.Columns.Add("Process name", 208);
        processListView.Columns.Add("Window title", 300);

        // Replace the existing event handlers with these:
        processListView.ColumnWidthChanging += (sender, e) => e.Cancel = true;
        processListView.ColumnWidthChanged += (sender, e) =>
        {
            if (!isAdjustingColumns)
            {
                isAdjustingColumns = true;
                try
                {
                    // Set the columns to their fixed widths
                    if (processListView.Columns.Count >= 3)
                    {
                        processListView.Columns[0].Width = 22;
                        processListView.Columns[1].Width = 208;
                        processListView.Columns[2].Width = 300;
                    }
                }
                finally
                {
                    isAdjustingColumns = false;
                }
            }
        };

            selectButton = new Button
            {
                Text = "Select",
                Location = new Point(590 - margin - 100 + 1, 400),
                Size = new Size(100, 30),
                BackColor = ColorTranslator.FromHtml("#3D3D3D"),
                ForeColor = Color.White
            };
            selectButton.Click += SelectButton_Click;

            manualGameButton = new Button
            {
                Text = "Add manually",
                Location = new Point(margin - 17, 400),
                Size = new Size(100, 30),
                BackColor = ColorTranslator.FromHtml("#3D3D3D"),
                ForeColor = Color.White
            };
            manualGameButton.Click += ManualGameButton_Click;

            this.Controls.Add(processListView);
            this.Controls.Add(selectButton);
            this.Controls.Add(manualGameButton);

            updateTimer = new System.Windows.Forms.Timer
            {
                Interval = 3000
            };
            updateTimer.Tick += UpdateTimer_Tick;

            updateTimer = new System.Windows.Forms.Timer
            {
                Interval = 3000 // Update every 3 seconds
            };
            updateTimer.Tick += UpdateTimer_Tick;

            this.FormClosing += ProcessPickerForm_FormClosing;
        }

        private void LoadProcesses()
        {
            var processes = GetProcesses();
            UpdateProcessList(processes);
        }

        private static List<(string Name, string Title, Icon? Icon)> GetProcesses()
        {
            var result = new List<(string Name, string Title, Icon? Icon)>();
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (!string.IsNullOrEmpty(process.MainWindowTitle))
                    {
                        string name = $"{process.ProcessName}.exe";
                        string title = process.MainWindowTitle;
                        Icon? icon = null;

                        try
                        {
                            string? fileName = process.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(fileName))
                            {
                                icon = Icon.ExtractAssociatedIcon(fileName);
                            }
                        }
                        catch
                        {
                            // Ignore any icon extraction errors
                        }

                        result.Add((name, title, icon));
                    }
                }
                catch
                {
                    // Ignore any processes that can't be accessed
                }
                finally
                {
                    process.Dispose();
                }
            }
            return result;
        }

        private void UpdateProcessList(List<(string Name, string Title, Icon? Icon)> processes)
        {
            processListView.BeginUpdate();
            try
            {
                processListView.Items.Clear();
                iconImageList.Images.Clear();
                iconCache.Clear();

                foreach (var (name, title, icon) in processes)
                {
                    var item = new ListViewItem(new[] { "", name, title });

                    if (icon != null)
                    {
                        if (!iconCache.TryGetValue(name, out int imageIndex))
                        {
                            imageIndex = iconImageList.Images.Count;
                            iconImageList.Images.Add(name, icon);
                            iconCache[name] = imageIndex;
                        }
                        else
                        {
                            icon.Dispose(); // Dispose of the icon if it's already in the cache
                        }
                        item.ImageIndex = imageIndex;
                    }
                    else
                    {
                        item.ImageIndex = 0; // Use default icon
                    }

                    processListView.Items.Add(item);
                }
            }
            finally
            {
                processListView.EndUpdate();
            }
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            var processes = GetProcesses();
            UpdateProcessList(processes);
        }

        private void ProcessPickerForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            updateTimer.Stop();
            foreach (var icon in iconImageList.Images)
            {
                if (icon is Icon disposableIcon)
                {
                    disposableIcon.Dispose();
                }
            }
            iconImageList.Images.Clear();
            iconCache.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                updateTimer?.Dispose();
                iconImageList?.Dispose();
                processListView?.Dispose();
                selectButton?.Dispose();
                manualGameButton?.Dispose();

                foreach (var icon in iconImageList.Images)
                {
                    if (icon is Icon disposableIcon)
                    {
                        disposableIcon.Dispose();
                    }
                }
            }
            base.Dispose(disposing);
        }

        private void SelectButton_Click(object? sender, EventArgs e)
        {
            if (processListView.SelectedItems.Count > 0)
            {
                SelectedProcess = processListView.SelectedItems[0].SubItems[1].Text;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                MessageBox.Show("Please select a process.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            updateTimer.Start();
        }

        private void ManualGameButton_Click(object? sender, EventArgs e)
        {
            using var inputForm = new ManualGameInputForm();
            if (inputForm.ShowDialog() == DialogResult.OK)
            {
                SelectedProcess = inputForm.GameName;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }
    }

    public class PauseManager
    {
        private static readonly object _lock = new object();
        private bool _isPaused;
        private DateTime _pauseEndTime;

        public bool IsPaused
        {
            get
            {
                lock (_lock)
                {
                    if (_isPaused && DateTime.Now >= _pauseEndTime)
                    {
                        _isPaused = false;
                    }
                    return _isPaused;
                }
            }
        }

        public void Pause(TimeSpan duration)
        {
            lock (_lock)
            {
                _isPaused = true;
                _pauseEndTime = DateTime.Now.Add(duration);
            }
        }

        public void PauseIndefinitely()
        {
            lock (_lock)
            {
                _isPaused = true;
                _pauseEndTime = DateTime.MaxValue;
            }
        }

        public void Unpause()
        {
            lock (_lock)
            {
                _isPaused = false;
            }
        }
    }

    public class ManualGameInputForm : Form
    {
        private TextBox gameNameTextBox = null!;
        private Button okButton = null!;
        private Button cancelButton = null!;
        public string? GameName { get; private set; }

        public ManualGameInputForm()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = "Add game manually";
            this.Size = new Size(305, 155);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = ColorTranslator.FromHtml("#1E1E1E");
            this.ForeColor = Color.White;

            Label instructionLabel = new()
            {
                Text = "Enter the name of the game executable\n(including .exe):",
                Location = new Point(10, 10),
                Size = new Size(280, 40),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.White
            };

            gameNameTextBox = new TextBox
            {
                Location = new Point(10, 55),
                Size = new Size(270, 20),
                BackColor = ColorTranslator.FromHtml("#2D2D2D"),
                ForeColor = Color.White
            };

            okButton = new Button
            {
                Text = "OK",
                Location = new Point(121, 85),
                Size = new Size(75, 25),
                BackColor = ColorTranslator.FromHtml("#3D3D3D"),
                ForeColor = Color.White,
                DialogResult = DialogResult.OK
            };
            okButton.Click += OkButton_Click;

            cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(206, 85),
                Size = new Size(75, 25),
                BackColor = ColorTranslator.FromHtml("#3D3D3D"),
                ForeColor = Color.White,
                DialogResult = DialogResult.Cancel
            };

            this.Controls.Add(instructionLabel);
            this.Controls.Add(gameNameTextBox);
            this.Controls.Add(okButton);
            this.Controls.Add(cancelButton);

            this.AcceptButton = okButton;
            this.CancelButton = cancelButton;
        }

        private void OkButton_Click(object? sender, EventArgs e)
        {
            GameName = gameNameTextBox.Text.Trim();
            if (IsValidGameExecutable(GameName))
            {
                DialogResult = DialogResult.OK;
            }
            else
            {
                MessageBox.Show("Please enter a valid game executable name (e.g., 'gamename.exe').", "Invalid Input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
            }
        }

        private static bool IsValidGameExecutable(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.Length <= 4)
            {
                return false;
            }

            string pattern = @"^[a-zA-Z0-9\s\-_]+\.exe$";
            return Regex.IsMatch(name, pattern);
        }
    }
}