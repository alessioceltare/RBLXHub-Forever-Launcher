using System.Diagnostics;
using System.Text.Json;

namespace RBLXHubLauncher;

public sealed partial class MainWindow
{
    private Label? _dllPathLabel;
    private Label? _rccPathLabel;
    private static string RccSelectionPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RBLXHubLauncher",
        "rcc-selection.json");

    private void AddLauncherSettingsPanel()
    {
        string launcherPath = _selectedRblxHubPath ?? "Not selected";
        string dllPath = _selectedRblxHubPath is null
            ? "Not selected"
            : Path.Combine(_selectedRblxHubPath, "gubbyRCC2.0.dll");
        string rccPath = LoadSavedRccPath() ?? "Not selected";

        var panel = new Panel
        {
            Location = new Point(24, 239),
            Size = new Size(931, 300),
            BackColor = Color.Transparent
        };
        Controls.Add(panel);
        _launcherSettingsControls.Add(panel);

        AddPathSetting(
            panel,
            "RBLXHUB Forever Path:",
            launcherPath,
            new Point(8, 4),
            "Change Path",
            BrowseForLauncherFolder);
        _dllPathLabel = AddPathSetting(
            panel,
            "DLL:",
            dllPath,
            new Point(8, 98),
            "Change DLL",
            BrowseForDll);
        _rccPathLabel = AddPathSetting(
            panel,
            "[OPTIONAL] Select RCC:",
            rccPath,
            new Point(8, 192),
            "Select RCC",
            BrowseForRcc);

        var cookieHeading = CreateSettingsLabel(
            "Edit Cookies",
            new Point(626, 8),
            new Size(280, 25),
            15);
        cookieHeading.TextAlign = ContentAlignment.MiddleCenter;
        panel.Controls.Add(cookieHeading);

        Button firstCookie = AddHostingButton(
            panel,
            "Edit 1st cookie file",
            new Point(650, 38),
            new Size(240, 34),
            13);
        firstCookie.Click += (_, _) => OpenCookieFile(1);

        Button secondCookie = AddHostingButton(
            panel,
            "Edit 2nd cookie file",
            new Point(650, 80),
            new Size(240, 34),
            13);
        secondCookie.Click += (_, _) => OpenCookieFile(2);

        var headlessLabel = CreateSettingsLabel(
            "Start Headless Studio When Hosting",
            new Point(626, 138),
            new Size(280, 40),
            14);
        headlessLabel.TextAlign = ContentAlignment.MiddleCenter;
        panel.Controls.Add(headlessLabel);

        Button headlessToggle = AddHostingButton(
            panel,
            "OFF",
            new Point(650, 181),
            new Size(240, 34),
            13);
        headlessToggle.BackColor = Color.FromArgb(64, 64, 64);
        headlessToggle.FlatAppearance.MouseOverBackColor = headlessToggle.BackColor;
        headlessToggle.FlatAppearance.MouseDownBackColor = headlessToggle.BackColor;
        headlessToggle.Click += (_, _) =>
        {
            _headlessStudioEnabled = !_headlessStudioEnabled;
            headlessToggle.Text = _headlessStudioEnabled ? "ON" : "OFF";
            headlessToggle.BackColor = _headlessStudioEnabled
                ? Color.FromArgb(53, 65, 194)
                : Color.FromArgb(64, 64, 64);
            headlessToggle.FlatAppearance.MouseOverBackColor = headlessToggle.BackColor;
            headlessToggle.FlatAppearance.MouseDownBackColor = headlessToggle.BackColor;
        };

        var rccPriorityLabel = CreateSettingsLabel(
            "Prioritize RCC Over Studio",
            new Point(626, 218),
            new Size(280, 25),
            14);
        rccPriorityLabel.TextAlign = ContentAlignment.MiddleCenter;
        panel.Controls.Add(rccPriorityLabel);

        Button rccPriorityToggle = AddHostingButton(
            panel,
            _prioritizeRccOverStudio ? "ON" : "OFF",
            new Point(650, 246),
            new Size(240, 34),
            13);
        rccPriorityToggle.BackColor = _prioritizeRccOverStudio
            ? Color.FromArgb(53, 65, 194)
            : Color.FromArgb(64, 64, 64);
        rccPriorityToggle.FlatAppearance.MouseOverBackColor = rccPriorityToggle.BackColor;
        rccPriorityToggle.FlatAppearance.MouseDownBackColor = rccPriorityToggle.BackColor;
        rccPriorityToggle.Click += (_, _) =>
        {
            _prioritizeRccOverStudio = !_prioritizeRccOverStudio;
            rccPriorityToggle.Text = _prioritizeRccOverStudio ? "ON" : "OFF";
            rccPriorityToggle.BackColor = _prioritizeRccOverStudio
                ? Color.FromArgb(53, 65, 194)
                : Color.FromArgb(64, 64, 64);
            rccPriorityToggle.FlatAppearance.MouseOverBackColor = rccPriorityToggle.BackColor;
            rccPriorityToggle.FlatAppearance.MouseDownBackColor = rccPriorityToggle.BackColor;
        };

        panel.Visible = false;
    }

    private Label AddPathSetting(
        Control parent,
        string title,
        string initialPath,
        Point location,
        string buttonText,
        Action<Label> browseAction)
    {
        var titleLabel = CreateSettingsLabel(
            title,
            location,
            new Size(555, 20),
            14);
        parent.Controls.Add(titleLabel);

        var pathLabel = CreateSettingsLabel(
            initialPath,
            new Point(location.X, location.Y + 22),
            new Size(555, 24),
            11);
        pathLabel.ForeColor = Color.FromArgb(190, 190, 190);
        pathLabel.AutoEllipsis = true;
        pathLabel.Tag = initialPath;
        parent.Controls.Add(pathLabel);

        Button browseButton = AddHostingButton(
            parent,
            buttonText,
            new Point(location.X, location.Y + 50),
            new Size(145, 34),
            13);
        browseButton.Click += (_, _) => browseAction(pathLabel);

        return pathLabel;
    }

    private Label CreateSettingsLabel(string text, Point location, Size size, float fontSize)
    {
        return new Label
        {
            Text = text,
            Location = location,
            Size = size,
            Font = CreateInterfaceFont(fontSize),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            UseCompatibleTextRendering = true
        };
    }

    private void BrowseForLauncherFolder(Label pathLabel)
    {
        string currentPath = pathLabel.Tag as string ?? AppContext.BaseDirectory;
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the RBLXHUB Forever folder",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(currentPath)
                ? currentPath
                : AppContext.BaseDirectory
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetSelectedPath(pathLabel, dialog.SelectedPath);
        }
    }

    private void BrowseForDll(Label pathLabel)
    {
        BrowseForFile(
            pathLabel,
            "DLL files (*.dll)|*.dll|All files (*.*)|*.*",
            "Select DLL",
            _selectedRblxHubPath);
    }

    private void BrowseForRcc(Label pathLabel)
    {
        string? previousPath = pathLabel.Tag as string;
        BrowseForFile(
            pathLabel,
            "Executables (*.exe)|*.exe|All files (*.*)|*.*",
            "Select RCC executable",
            _selectedRblxHubPath);

        if (pathLabel.Tag is string selectedPath &&
            !string.Equals(selectedPath, previousPath, StringComparison.Ordinal))
        {
            SaveRccPath(selectedPath);
        }
    }

    private void OpenCookieFile(int cookieNumber)
    {
        if (string.IsNullOrWhiteSpace(_selectedRblxHubPath))
        {
            MessageBox.Show(
                this,
                "Select the RBLXHUB Forever folder in onboarding first.",
                "RBLXHUB folder not selected",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        string fileName = cookieNumber switch
        {
            1 => "DONTSHAREIDIOT.txt",
            2 => "DONTSHAREIDIOT2.txt",
            _ => throw new ArgumentOutOfRangeException(nameof(cookieNumber))
        };
        string cookiePath = Path.Combine(
            _selectedRblxHubPath,
            "webserver",
            "apache",
            "www",
            "DO_NOT_SHARE",
            fileName);

        if (!File.Exists(cookiePath))
        {
            MessageBox.Show(
                this,
                $"Cookie file not found:\n{cookiePath}",
                "Cookie file not found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo("notepad.exe")
            {
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add(cookiePath);
            Process.Start(startInfo);
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or
            InvalidOperationException)
        {
            MessageBox.Show(
                this,
                $"Could not open the cookie file in Notepad:\n\n{exception.Message}",
                "Could not open cookie file",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void BrowseForFile(
        Label pathLabel,
        string filter,
        string title,
        string? preferredDirectory = null)
    {
        string currentPath = pathLabel.Tag as string ?? string.Empty;
        string? initialDirectory = Path.GetDirectoryName(currentPath);
        if (string.IsNullOrWhiteSpace(initialDirectory) ||
            !Directory.Exists(initialDirectory))
        {
            initialDirectory = preferredDirectory is { } root && Directory.Exists(root)
                ? root
                : AppContext.BaseDirectory;
        }

        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            InitialDirectory = initialDirectory,
            FileName = File.Exists(currentPath) ? Path.GetFileName(currentPath) : string.Empty,
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetSelectedPath(pathLabel, dialog.FileName);
        }
    }

    private static void SetSelectedPath(Label pathLabel, string path)
    {
        pathLabel.Tag = path;
        pathLabel.Text = path;
    }

    /// <summary>
    /// Returns the DLL path currently configured in Launcher Settings: whatever the user
    /// browsed to via "Change DLL", or the default "gubbyRCC2.0.dll" inside the selected
    /// RBLXHUB Forever folder if they never overrode it. Returns null if neither is set.
    /// </summary>
    private string? GetConfiguredDllPath()
    {
        if (_dllPathLabel?.Tag is string explicitPath &&
            !string.IsNullOrWhiteSpace(explicitPath) &&
            !string.Equals(explicitPath, "Not selected", StringComparison.Ordinal))
        {
            return explicitPath;
        }

        return string.IsNullOrWhiteSpace(_selectedRblxHubPath)
            ? null
            : Path.Combine(_selectedRblxHubPath, "gubbyRCC2.0.dll");
    }

    private string? GetConfiguredRccPath()
    {
        return _rccPathLabel?.Tag is string selectedPath &&
            !string.IsNullOrWhiteSpace(selectedPath) &&
            !string.Equals(selectedPath, "Not selected", StringComparison.Ordinal)
                ? selectedPath
                : null;
    }

    private static string? LoadSavedRccPath()
    {
        if (!File.Exists(RccSelectionPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RccSelection>(
                File.ReadAllText(RccSelectionPath))?.Path;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            MessageBox.Show(
                $"Saved RCC selection could not be read.\n\n{exception.Message}",
                "RCC selection",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return null;
        }
    }

    private void SaveRccPath(string path)
    {
        try
        {
            string? directory = Path.GetDirectoryName(RccSelectionPath);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                RccSelectionPath,
                JsonSerializer.Serialize(new RccSelection(path)));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                $"Could not save the RCC selection.\n\n{exception.Message}",
                "RCC selection",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private sealed record RccSelection(string Path);
}