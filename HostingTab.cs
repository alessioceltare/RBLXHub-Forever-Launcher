using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RBLXHubLauncher;

public sealed partial class MainWindow
{
    private const uint ProcessAllAccess = 0x1F0FFF;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint PageReadWrite = 0x04;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(
        IntPtr hProcess, IntPtr address, uint size, uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(
        IntPtr hProcess, IntPtr address, byte[] buffer, uint size, out UIntPtr bytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(
        IntPtr hProcess, IntPtr attributes, uint stackSize,
        IntPtr startAddress, IntPtr parameter, uint flags, out uint threadId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandleA(string moduleName);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// Injects the hook DLL into the given process using LoadLibraryA via a remote thread.
    /// Waits briefly for kernel32 to be loaded before attempting, since the process may
    /// still be initializing right after Process.Start returns.
    /// </summary>
    private static void InjectHookDll(Process targetProcess, string dllPath)
    {
        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException("Hook DLL not found.", dllPath);
        }

        if (!WaitForProcessReady(targetProcess, TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException(
                "Timed out waiting for the target process to initialize.");
        }

        IntPtr hProcess = OpenProcess(ProcessAllAccess, false, targetProcess.Id);
        if (hProcess == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"OpenProcess failed (error {Marshal.GetLastWin32Error()}). " +
                "Try running the launcher as administrator.");
        }

        try
        {
            byte[] pathBytes = Encoding.ASCII.GetBytes(dllPath + "\0");
            IntPtr remoteBuffer = VirtualAllocEx(
                hProcess, IntPtr.Zero, (uint)pathBytes.Length,
                MemCommit | MemReserve, PageReadWrite);
            if (remoteBuffer == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"VirtualAllocEx failed (error {Marshal.GetLastWin32Error()}).");
            }

            if (!WriteProcessMemory(
                hProcess, remoteBuffer, pathBytes, (uint)pathBytes.Length, out _))
            {
                throw new InvalidOperationException(
                    $"WriteProcessMemory failed (error {Marshal.GetLastWin32Error()}).");
            }

            IntPtr loadLibraryAddress = GetProcAddress(
                GetModuleHandleA("kernel32.dll"), "LoadLibraryA");
            if (loadLibraryAddress == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not resolve LoadLibraryA.");
            }

            IntPtr remoteThread = CreateRemoteThread(
                hProcess, IntPtr.Zero, 0, loadLibraryAddress, remoteBuffer, 0, out _);
            if (remoteThread == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"CreateRemoteThread failed (error {Marshal.GetLastWin32Error()}).");
            }

            try
            {
                WaitForSingleObject(remoteThread, 5000);
            }
            finally
            {
                CloseHandle(remoteThread);
            }
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    private static bool WaitForProcessReady(Process process, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                process.Refresh();
                if (process.HasExited)
                {
                    return false;
                }

                foreach (ProcessModule module in process.Modules)
                {
                    if (module.ModuleName.Equals(
                        "kernel32.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Win32Exception)
            {
                // Modules aren't enumerable yet; retry.
            }
            catch (InvalidOperationException)
            {
                // Process may have exited between checks; retry/exit via HasExited above.
            }

            Thread.Sleep(50);
        }

        return false;
    }

    private readonly List<Control> _hostingControls = [];
    private readonly List<Font> _interfaceFonts = [];
    private Label _hostingOutputHeader = null!;
    private TextBox _hostingOutput = null!;
    private Button _webServerButton = null!;
    private Button _hostingButton = null!;
    private System.Windows.Forms.Timer _webServerMonitor = null!;
    private Process? _webServerStartCommand;
    private Process? _hostingProcess;
    private bool _headlessStudioEnabled;
    private bool _prioritizeRccOverStudio = true;
    private bool _webServerStarting;
    private bool _webServerStopping;
    private DateTime _webServerStartDeadline;

    private void AddHostingPanel()
    {
        var panel = new Panel
        {
            Location = new Point(20, 215),
            Size = new Size(1000, 1000),
        };
        Controls.Add(panel);
        _hostingControls.Add(panel);

        var outputBox = new RoundedPanel
        {
            Location = new Point(500, 62),
            Size = new Size(450, 270),
            BackColor = Color.Black,
            CornerRadius = 12
        };
        panel.Controls.Add(outputBox);

        _hostingOutputHeader = new Label
        {
            Text = "output",
            Location = new Point(855, 30),
            Size = new Size(130, 30),
            Font = CreateInterfaceFont(16),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        panel.Controls.Add(_hostingOutputHeader);

        _hostingOutput = new TextBox
        {
            Location = new Point(12, 8),
            Size = new Size(426, 246),
            Font = CreateInterfaceFont(16),
            ForeColor = Color.FromArgb(200, 200, 200),
            BackColor = Color.Black,
            BorderStyle = BorderStyle.None,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.None,
            WordWrap = true
        };
        outputBox.Controls.Add(_hostingOutput);

        _webServerButton = AddHostingButton(
            panel,
            "Start Webserver",
            new Point(0, 33),
            new Size(480, 60),
            20);
        _webServerButton.Click += ToggleWebServer;
        _webServerMonitor = new System.Windows.Forms.Timer { Interval = 1000 };
        _webServerMonitor.Tick += (_, _) => RefreshWebServerState();
        _webServerMonitor.Start();
        RefreshWebServerState();
        Button startStudioButton = AddHostingButton(
            panel,
            "Start Studio",
            new Point(0, 100),
            new Size(240, 44),
            16);
        startStudioButton.Click += StartStudio;
        Button startClientButton = AddHostingButton(
            panel,
            "Start Client",
            new Point(240, 100),
            new Size(240, 44),
            20);
        startClientButton.Click += StartClient;

        Button dropTarget = AddHostingButton(
            panel,
            "drop .rbxl file here",
            new Point(0, 150),
            new Size(480, 110),
            24);
        dropTarget.AllowDrop = true;
        dropTarget.DragEnter += RbxlFile_DragEnter;
        dropTarget.DragDrop += RbxlFile_DragDrop;

        _hostingButton = AddHostingButton(
            panel,
            "Start Hosting",
            new Point(0, 270),
            new Size(480, 60),
            20);
        _hostingButton.Click += ToggleHosting;
    }

    private void ToggleHosting(object? sender, EventArgs e)
    {
        if (_hostingProcess is { } process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }

                ClearHostingProcess(process, "Hosting stopped.");
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or
                System.ComponentModel.Win32Exception or
                NotSupportedException)
            {
                _hostingOutput.Text = $"Could not stop hosting: {exception.Message}";
                MessageBox.Show(
                    this,
                    $"Could not stop the hosting process:\n\n{exception.Message}",
                    "Stop hosting failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            return;
        }

        StartHosting(sender, e);
    }

    private void TrackHostingProcess(Process process, string startedMessage)
    {
        _hostingProcess = process;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(() => ClearHostingProcess(process, "Hosting process exited."));
            }
        };
        _hostingButton.Text = "Stop Hosting";
        _hostingOutput.Text = startedMessage;
    }

    private void ClearHostingProcess(Process process, string message)
    {
        if (!ReferenceEquals(_hostingProcess, process))
        {
            return;
        }

        _hostingProcess = null;
        _hostingButton.Text = "Start Hosting";
        _hostingOutput.Text = message;
        process.Dispose();
    }

    private static void ConfigureCapturedOutput(ProcessStartInfo startInfo)
    {
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
    }

    private void CaptureProcessOutput(Process process, bool disposeOnExit = false)
    {
        process.OutputDataReceived += (_, args) => AppendHostingOutput(args.Data);
        process.ErrorDataReceived += (_, args) => AppendHostingOutput(args.Data);
        if (disposeOnExit)
        {
            process.Exited += (_, _) => process.Dispose();
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    private void AppendHostingOutput(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => AppendHostingOutput(line));
            return;
        }

        const int maxOutputLength = 12000;
        string updatedText = string.IsNullOrEmpty(_hostingOutput.Text)
            ? line
            : $"{_hostingOutput.Text}{Environment.NewLine}{line}";
        _hostingOutput.Text = updatedText.Length > maxOutputLength
            ? updatedText[^maxOutputLength..]
            : updatedText;
        _hostingOutput.SelectionStart = _hostingOutput.TextLength;
        _hostingOutput.ScrollToCaret();
    }

    private void StartClient(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedRblxHubPath))
        {
            _hostingOutput.Text = "Complete onboarding to select the RBLXHUB folder.";
            return;
        }

        string clientDirectory = Path.Combine(_selectedRblxHubPath, "2026E");
        string clientExecutable = Path.Combine(clientDirectory, "launcherthingyrelease.exe");
        if (!File.Exists(clientExecutable))
        {
            _hostingOutput.Text = "launcherthingyrelease.exe not found in 2026E.";
            MessageBox.Show(
                this,
                $"Could not find the client launcher at:\n{clientExecutable}",
                "Client launcher not found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = clientExecutable,
                WorkingDirectory = clientDirectory,
            };
            ConfigureCapturedOutput(startInfo);
            Process clientProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The client launcher could not be started.");
            CaptureProcessOutput(clientProcess, disposeOnExit: true);
            _hostingOutput.Text = "Client launcher started from 2026E.";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            Win32Exception or
            IOException)
        {
            _hostingOutput.Text = $"Could not start the client launcher: {exception.Message}";
            MessageBox.Show(
                this,
                $"Could not start the client launcher:\n\n{exception.Message}",
                "Client launch failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void StartStudio(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedRblxHubPath))
        {
            _hostingOutput.Text = "Complete onboarding to select the RBLXHUB folder.";
            return;
        }

        string studioDirectory = Path.Combine(_selectedRblxHubPath, "2026EStudio");
        string studioExecutable = Path.Combine(studioDirectory, "RobloxStudioBeta.exe");
        if (!File.Exists(studioExecutable))
        {
            _hostingOutput.Text = "RobloxStudioBeta.exe not found in 2026EStudio.";
            MessageBox.Show(
                this,
                $"Could not find Roblox Studio at:\n{studioExecutable}",
                "Roblox Studio not found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = studioExecutable,
                WorkingDirectory = studioDirectory,
                UseShellExecute = false,
                WindowStyle = _headlessStudioEnabled
                    ? ProcessWindowStyle.Hidden
                    : ProcessWindowStyle.Normal
            };
            _ = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Roblox Studio could not be started.");
            _hostingOutput.Text = "Roblox Studio started normally from 2026EStudio.";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            Win32Exception or
            IOException)
        {
            _hostingOutput.Text = $"Could not start Roblox Studio: {exception.Message}";
            MessageBox.Show(
                this,
                $"Could not start Roblox Studio:\n\n{exception.Message}",
                "Roblox Studio launch failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void StartHosting(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedRblxHubPath))
        {
            _hostingOutput.Text = "Complete onboarding to select the RBLXHUB folder.";
            return;
        }

        string? configuredRccPath = GetConfiguredRccPath();
        if (_prioritizeRccOverStudio &&
            !string.IsNullOrWhiteSpace(configuredRccPath))
        {
            StartConfiguredRcc(configuredRccPath);
            return;
        }

        string? configuredDllPath = GetConfiguredDllPath();
        if (string.IsNullOrWhiteSpace(configuredDllPath) || !File.Exists(configuredDllPath))
        {
            _hostingOutput.Text = "hook DLL not found in the selected folder.";
            MessageBox.Show(
                this,
                $"Could not find the hook DLL at:\n{configuredDllPath}\n\n" +
                "Set it under Launcher Settings \u2192 DLL, or place gubbyRCC2.0.dll " +
                "in the RBLXHUB Forever folder.",
                "Hook DLL not found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        string dllPath = configuredDllPath;

        string studioDirectory = Path.Combine(_selectedRblxHubPath, "2026EStudio");
        string studioExecutable = Path.Combine(studioDirectory, "RobloxStudioBeta.exe");
        if (!File.Exists(studioExecutable))
        {
            _hostingOutput.Text = "RobloxStudioBeta.exe not found in 2026EStudio.";
            MessageBox.Show(
                this,
                $"Could not find Roblox Studio at:\n{studioExecutable}",
                "Roblox Studio not found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = studioExecutable,
            WorkingDirectory = studioDirectory,
            UseShellExecute = false,
            WindowStyle = _headlessStudioEnabled
                ? ProcessWindowStyle.Hidden
                : ProcessWindowStyle.Normal
        };
        string[] arguments =
        [
            "-task", "StartServer",
            "-port", "53640",
            "-placeId", "1",
            "-universeId", "1",
            "-creatorId", "1",
            "-creatorType", "0",
            "-placeVersion", "1",
            "-numTestServerPlayersUponStartup", "0",
            "-instanceId", "StudioServer",
            "-gameId", "Test",
            "-Console"
        ];
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process studioProcess;
        try
        {
            studioProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Roblox Studio could not be started.");
            TrackHostingProcess(
                studioProcess,
                "Roblox Studio server launched from 2026EStudio.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            IOException)
        {
            _hostingOutput.Text = $"Could not start Roblox Studio: {exception.Message}";
            MessageBox.Show(
                this,
                $"Could not start Roblox Studio:\n\n{exception.Message}",
                "Roblox Studio launch failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        _hostingOutput.Text = "Roblox Studio launched, injecting hook.dll...";

        Task.Run(() =>
        {
            try
            {
                InjectHookDll(studioProcess, dllPath);
                BeginInvoke(() =>
                {
                    if (ReferenceEquals(_hostingProcess, studioProcess))
                        _hostingOutput.Text = "Roblox Studio server launched and hook.dll injected.";
                });
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or
                FileNotFoundException)
            {
                BeginInvoke(() =>
                {
                    if (!ReferenceEquals(_hostingProcess, studioProcess))
                    {
                        return;
                    }

                    _hostingOutput.Text = $"Server launched, but injection failed: {exception.Message}";
                    MessageBox.Show(
                        this,
                        $"Roblox Studio started, but hook.dll could not be injected:\n\n{exception.Message}",
                        "Injection failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                });
            }
        });
    }

    private void StartConfiguredRcc(string rccExecutable)
    {
        if (!File.Exists(rccExecutable))
        {
            _hostingOutput.Text = "The selected RCC executable could not be found.";
            MessageBox.Show(
                this,
                $"Could not find the selected RCC executable:\n{rccExecutable}",
                "RCC executable not found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        string rccDirectory = Path.GetDirectoryName(rccExecutable)
            ?? AppContext.BaseDirectory;
        var startInfo = new ProcessStartInfo
        {
            FileName = rccExecutable,
            WorkingDirectory = rccDirectory,
            WindowStyle = _headlessStudioEnabled
                ? ProcessWindowStyle.Hidden
                : ProcessWindowStyle.Normal
        };
        ConfigureCapturedOutput(startInfo);

        try
        {
            Process rccProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The selected RCC could not be started.");
            TrackHostingProcess(rccProcess, "Selected RCC executable started for hosting.");
            CaptureProcessOutput(rccProcess);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            IOException)
        {
            _hostingOutput.Text = $"Could not start the selected RCC: {exception.Message}";
            MessageBox.Show(
                this,
                $"Could not start the selected RCC executable:\n\n{exception.Message}",
                "RCC launch failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private string? GetWebServerRoot()
    {
        return string.IsNullOrWhiteSpace(_selectedRblxHubPath)
            ? null
            : Path.Combine(_selectedRblxHubPath, "webserver", "apache");
    }

    private string? GetRunningWebServerExecutable()
    {
        string? apacheRoot = GetWebServerRoot();
        if (apacheRoot is null)
        {
            return null;
        }

        string expectedPath = Path.GetFullPath(Path.Combine(apacheRoot, "bin", "httpd.exe"));
        foreach (Process process in Process.GetProcessesByName("httpd"))
        {
            using (process)
            {
                try
                {
                    string processPath = process.MainModule?.FileName ?? string.Empty;
                    if (processPath.Length > 0 &&
                        string.Equals(
                            Path.GetFullPath(processPath),
                            expectedPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return expectedPath;
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or
                    System.ComponentModel.Win32Exception or
                    NotSupportedException)
                {
                    // An inaccessible process cannot be safely matched to this Apache install.
                }
            }
        }

        return null;
    }

    private void RefreshWebServerState()
    {
        bool running = GetRunningWebServerExecutable() is not null;
        if (running)
        {
            _webServerStarting = false;
            _webServerStopping = false;
            DisposeExitedWebServerStartCommand();
            _webServerButton.Text = "Stop Webserver";
            _webServerButton.Enabled = true;
            _hostingOutput.Text = "Webserver running";
            return;
        }

        if (_webServerStarting)
        {
            if (_webServerStartCommand is { HasExited: true } startCommand)
            {
                int exitCode = startCommand.ExitCode;
                startCommand.Dispose();
                _webServerStartCommand = null;
                _webServerStarting = false;
                _webServerButton.Text = "Start Webserver";
                _webServerButton.Enabled = true;
                _hostingOutput.Text = $"Webserver did not start (start.bat exited with code {exitCode}).";
                return;
            }

            if (DateTime.Now < _webServerStartDeadline)
            {
                _webServerButton.Text = "Starting...";
                _webServerButton.Enabled = false;
                _hostingOutput.Text = "Starting webserver...";
                return;
            }

            _webServerStarting = false;
            _webServerButton.Text = "Start Webserver";
            _webServerButton.Enabled = true;
            _hostingOutput.Text = "Webserver process was not detected. Check Apache logs.";
            return;
        }

        if (_webServerStopping)
        {
            _webServerStopping = false;
            DisposeExitedWebServerStartCommand();
            _webServerButton.Text = "Start Webserver";
            _webServerButton.Enabled = true;
            _hostingOutput.Text = "Webserver stopped";
            return;
        }

        _webServerButton.Text = "Start Webserver";
        _webServerButton.Enabled = true;
        DisposeExitedWebServerStartCommand();
    }

    private void DisposeExitedWebServerStartCommand()
    {
        if (_webServerStartCommand is not { HasExited: true } exitedCommand)
        {
            return;
        }

        exitedCommand.Dispose();
        _webServerStartCommand = null;
    }

    private void ToggleWebServer(object? sender, EventArgs e)
    {
        if (GetRunningWebServerExecutable() is not null)
        {
            StopWebServer();
        }
        else
        {
            StartWebServer();
        }
    }

    private void StartWebServer()
    {
        string? apacheRoot = GetWebServerRoot();
        string? rblxHubRoot = _selectedRblxHubPath;
        if (apacheRoot is null || rblxHubRoot is null)
        {
            _hostingOutput.Text = "Complete onboarding to select the webserver folder.";
            return;
        }

        string batchPath = Path.Combine(rblxHubRoot, "webserver", "start.bat");
        if (!File.Exists(batchPath))
        {
            _hostingOutput.Text = "webserver\\start.bat not found.";
            MessageBox.Show(
                this,
                $"Could not find the webserver start script:\n{batchPath}",
                "Webserver script not found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _webServerStartCommand?.Dispose();
            var startInfo = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(batchPath)!
            };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(batchPath);

            _webServerStartCommand = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start webserver\\start.bat.");
            _webServerStartCommand.StandardInput.Close();
            CaptureProcessOutput(_webServerStartCommand);
            _webServerStarting = true;
            _webServerStartDeadline = DateTime.Now.AddSeconds(15);
            _hostingOutput.Text = "Starting webserver...";
            _webServerButton.Text = "Starting...";
            _webServerButton.Enabled = false;
            _webServerMonitor.Start();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            IOException)
        {
            _hostingOutput.Text = $"Could not start webserver: {exception.Message}";
            MessageBox.Show(
                this,
                $"Could not run webserver\\start.bat:\n\n{exception.Message}",
                "Webserver start failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            _webServerButton.Text = "Start Webserver";
            _webServerButton.Enabled = true;
        }
    }

    private void StopWebServer()
    {
        if (string.IsNullOrWhiteSpace(_selectedRblxHubPath))
        {
            _hostingOutput.Text = "Complete onboarding to select the webserver folder.";
            return;
        }

        string webServerDirectory = Path.Combine(_selectedRblxHubPath, "webserver");
        string batchPath = Path.Combine(webServerDirectory, "stop.bat");
        if (!File.Exists(batchPath))
        {
            _hostingOutput.Text = "webserver\\stop.bat not found.";
            MessageBox.Show(
                this,
                $"Could not find the webserver stop script:\n{batchPath}",
                "Webserver script not found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var stopInfo = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = webServerDirectory
            };
            stopInfo.Arguments = $"/d /c \"\"{batchPath}\" < NUL\"";

            using Process stopCommand = Process.Start(stopInfo)
                ?? throw new InvalidOperationException("Could not start webserver\\stop.bat.");
            CaptureProcessOutput(stopCommand);
            if (!stopCommand.WaitForExit(8000))
            {
                _webServerStopping = true;
                _webServerButton.Text = "Stopping...";
                _webServerButton.Enabled = false;
                _hostingOutput.Text = "webserver\\stop.bat is still running...";
                _webServerMonitor.Start();
                return;
            }

            if (stopCommand.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"webserver\\stop.bat exited with code {stopCommand.ExitCode}.");
            }

            _hostingOutput.Text = "Stopping webserver...";
            _webServerStopping = true;
            _webServerButton.Text = "Stopping...";
            _webServerButton.Enabled = false;
            _webServerMonitor.Start();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            IOException)
        {
            _hostingOutput.Text = $"Could not stop webserver: {exception.Message}";
            MessageBox.Show(
                this,
                $"Could not run webserver\\stop.bat:\n\n{exception.Message}",
                "Webserver stop failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            _webServerStopping = false;
            RefreshWebServerState();
        }
    }

    private Button AddHostingButton(
        Control parent,
        string text,
        Point location,
        Size size,
        float fontSize)
    {
        var button = new Button
        {
            Text = text,
            Location = location,
            Size = size,
            Region = CreateRoundedRegion(new Rectangle(Point.Empty, size), 5),
            Font = CreateInterfaceFont(fontSize),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(53, 65, 194),
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleCenter,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = button.BackColor;
        button.FlatAppearance.MouseDownBackColor = button.BackColor;
        parent.Controls.Add(button);
        return button;
    }

    private Font CreateInterfaceFont(float size)
    {
        var font = new Font(_navigationFontFamily, size, FontStyle.Regular);
        _interfaceFonts.Add(font);
        return font;
    }

    private static void RbxlFile_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true &&
            e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files &&
            File.Exists(files[0]) &&
            Path.GetExtension(files[0]).Equals(".rbxl", StringComparison.OrdinalIgnoreCase))
        {
            e.Effect = DragDropEffects.Copy;
        }
        else
        {
            e.Effect = DragDropEffects.None;
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        private int _cornerRadius;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CornerRadius
        {
            get => _cornerRadius;
            set
            {
                _cornerRadius = Math.Max(0, value);
                UpdateRoundedRegion();
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateRoundedRegion();
        }

        private void UpdateRoundedRegion()
        {
            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            if (_cornerRadius == 0)
            {
                Region? existingRegion = Region;
                Region = null;
                existingRegion?.Dispose();
                return;
            }

            int diameter = Math.Min(_cornerRadius * 2, Math.Min(Width, Height));
            using var path = new GraphicsPath();
            path.AddArc(0, 0, diameter, diameter, 180, 90);
            path.AddArc(Width - diameter, 0, diameter, diameter, 270, 90);
            path.AddArc(Width - diameter, Height - diameter, diameter, diameter, 0, 90);
            path.AddArc(0, Height - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            Region? previousRegion = Region;
            Region = new Region(path);
            previousRegion?.Dispose();
        }
    }

    private void RbxlFile_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files)
        {
            _hostingOutput.Text = "one .rbxl file only";
            return;
        }

        string sourcePath = files[0];
        if (!File.Exists(sourcePath) ||
            !Path.GetExtension(sourcePath).Equals(".rbxl", StringComparison.OrdinalIgnoreCase))
        {
            _hostingOutput.Text = "select a .rbxl file";
            return;
        }

        string robloxDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox");
        string destinationPath = Path.Combine(robloxDirectory, "server.rbxl");

        try
        {
            if (string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(destinationPath),
                StringComparison.OrdinalIgnoreCase))
            {
                _hostingOutput.Text = @"File is already at %LOCALAPPDATA%\Roblox\server.rbxl";
                return;
            }

            Directory.CreateDirectory(robloxDirectory);
            if (File.Exists(destinationPath) &&
                MessageBox.Show(
                    this,
                    "server.rbxl already exists in the Roblox folder. Replace it?",
                    "Replace server.rbxl",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                _hostingOutput.Text = "File move cancelled.";
                return;
            }

            File.Copy(sourcePath, destinationPath, overwrite: true);
            _hostingOutput.Text = @"Copied to %LOCALAPPDATA%\Roblox\server.rbxl";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            _hostingOutput.Text = $"Could not move file: {exception.Message}";
            MessageBox.Show(
                this,
                $"Could not copy the selected file to:\n{destinationPath}\n\n{exception.Message}",
                "RBXL file copy failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}