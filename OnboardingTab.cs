using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text.Json;

namespace RBLXHubLauncher;

public sealed partial class MainWindow
{
    private const string CertificateDirectoryRelativePath =
        @"webserver\apache\certificats";
    private const string HostsEntriesSearchPattern = "hosts*.txt";
    private readonly List<Control> _onboardingControls = [];
    private string? _selectedRblxHubPath;
    private bool _pathStepComplete;
    private bool _certificatesStepComplete;
    private bool _hostsStepComplete;
    private Button _selectPathButton = null!;
    private Button _installCertificatesButton = null!;
    private Button _addHostsButton = null!;
    private Button _finishOnboardingButton = null!;
    private Label _onboardingPathStatus = null!;
    private Label _certificateStatus = null!;
    private Label _hostsStatus = null!;

    private static string OnboardingStatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RBLXHubLauncher",
        "onboarding.json");

    private static string? FindHostsEntriesFile(string rblxHubPath)
    {
        string webserverDirectory = Path.Combine(rblxHubPath, "webserver");
        return Directory.Exists(webserverDirectory)
            ? Directory.EnumerateFiles(
                    webserverDirectory,
                    HostsEntriesSearchPattern,
                    SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
            : null;
    }

    private static void SaveOnboardingState(bool completed, string rblxHubPath)
    {
        string stateDirectory = Path.GetDirectoryName(OnboardingStatePath)
            ?? throw new InvalidOperationException("The onboarding settings directory is unavailable.");
        Directory.CreateDirectory(stateDirectory);
        string temporaryPath = OnboardingStatePath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(new OnboardingState(completed, rblxHubPath)));
        File.Move(temporaryPath, OnboardingStatePath, overwrite: true);
    }

    private void LoadSavedOnboardingState()
    {
        if (File.Exists(OnboardingStatePath))
        {
            try
            {
                OnboardingState? savedState = JsonSerializer.Deserialize<OnboardingState>(
                    File.ReadAllText(OnboardingStatePath));
                if (savedState?.Completed == true)
                {
                    _selectedRblxHubPath = savedState.RblxHubPath;
                    _onboardingCompleted = true;
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                JsonException)
            {
                MessageBox.Show(
                    this,
                    $"Saved onboarding status could not be read. Setup will be shown again.\n\n{exception.Message}",
                    "RBLXHub Launcher",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
    }

    private void InitializeOnboarding()
    {
        if (_onboardingCompleted)
        {
            return;
        }

        foreach (Button button in _navigationButtons)
        {
            button.Visible = false;
        }

        _onboardingActive = true;
        SetTabControlsVisible(_hostingControls, false);
        SetTabControlsVisible(_playerSettingsControls, false);
        SetTabControlsVisible(_launcherSettingsControls, false);

        AddOnboardingPanel();
    }

    private void AddOnboardingPanel()
    {
        var overlay = new OnboardingOverlay
        {
            Location = Point.Empty,
            Size = ClientSize
        };

        const int iconSize = 90;
        const int logoTextWidth = 250;
        const int logoGap = 10;
        int logoWidth = iconSize + logoGap + logoTextWidth;
        int logoLeft = (ClientSize.Width - logoWidth) / 2;

        var icon = new PictureBox
        {
            Image = _logoIcon,
            Location = new Point(logoLeft, 28),
            Size = new Size(iconSize, iconSize),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        overlay.Controls.Add(icon);

        var logoText = new OutlinedLogoText
        {
            Text = "Launcher",
            Location = new Point(logoLeft + iconSize + logoGap, 30),
            Size = new Size(logoTextWidth, iconSize),
            FontFamily = _logoFontFamily,
            FontSize = 38,
            TextAlign = StringAlignment.Near,
            BackColor = Color.Transparent
        };
        overlay.Controls.Add(logoText);

        var heading = new OutlinedLogoText
        {
            Text = "ONBOARDING",
            Location = new Point(0, 126),
            Size = new Size(ClientSize.Width, 58),
            FontFamily = _logoFontFamily,
            FontSize = 38,
            TextAlign = StringAlignment.Center,
            BackColor = Color.Transparent
        };
        overlay.Controls.Add(heading);

        int stepsTop = 222;
        int finishTop = ClientSize.Height - 65;
        int stepSpacing = (finishTop - stepsTop) / 3;

        AddOnboardingStep(
            overlay,
            stepsTop,
            "Select Path to RBLXHUB Forever",
            "Select Path",
            34,
            out _onboardingPathStatus,
            out _selectPathButton);
        _selectPathButton.Click += SelectRblxHubPath;

        AddOnboardingStep(
            overlay,
            stepsTop + stepSpacing,
            "Install Certificates",
            "Install",
            34,
            out _certificateStatus,
            out _installCertificatesButton);
        _installCertificatesButton.Enabled = false;
        _installCertificatesButton.Click += InstallCertificates;

        AddOnboardingStep(
            overlay,
            stepsTop + stepSpacing * 2,
            "Add host entries",
            "Add Entries",
            34,
            out _hostsStatus,
            out _addHostsButton);
        _addHostsButton.Enabled = false;
        _addHostsButton.Click += AddHostsEntries;

        _finishOnboardingButton = AddHostingButton(
            overlay,
            "Finish",
            new Point(ClientSize.Width - 280, finishTop),
            new Size(258, 52),
            21);
        _finishOnboardingButton.Enabled = false;
        _finishOnboardingButton.Click += FinishOnboarding;

        var credit = new Label
        {
            Text = "ui made by dystamyurm",
            Location = new Point(ClientSize.Width - 270, ClientSize.Height - 25),
            Size = new Size(220, 16),
            Font = CreateInterfaceFont(9),
            ForeColor = Color.FromArgb(63, 63, 63),
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Color.Transparent
        };
        overlay.Controls.Add(credit);

        Controls.Add(overlay);
        if (_globalBanner is not null)
        {
            overlay.Controls.Add(_globalBanner);
            _globalBanner.Location = new Point(0, 190);
            _globalBanner.Size = new Size(ClientSize.Width, 26);
            _globalBanner.BringToFront();
        }
        _onboardingControls.Add(overlay);
        overlay.BringToFront();
    }

    private void AddOnboardingStep(
        Control parent,
        int top,
        string title,
        string buttonText,
        int buttonOffset,
        out Label status,
        out Button actionButton)
    {
        var titleLabel = new Label
        {
            Text = title,
            Location = new Point(24, top),
            Size = new Size(650, 30),
            Font = CreateInterfaceFont(19),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };
        parent.Controls.Add(titleLabel);

        status = new Label
        {
            Text = "Required",
            Location = new Point(0, 0),
            Size = Size.Empty,
            Font = CreateInterfaceFont(10),
            ForeColor = Color.FromArgb(175, 175, 175),
            Visible = false
        };

        actionButton = AddHostingButton(
            parent,
            buttonText,
            new Point(24, top + buttonOffset),
            new Size(184, 40),
            15);
    }

    private void SelectRblxHubPath(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the RBLXHUB Forever folder",
            UseDescriptionForTitle = true,
            InitialDirectory = _selectedRblxHubPath is { } path && Directory.Exists(path)
                ? path
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        string certificateDirectory = Path.Combine(
            dialog.SelectedPath,
            CertificateDirectoryRelativePath);
        string? hostsEntriesPath = FindHostsEntriesFile(dialog.SelectedPath);
        var missingResources = new List<string>();
        if (!Directory.Exists(certificateDirectory))
        {
            missingResources.Add(CertificateDirectoryRelativePath);
        }
        else if (!Directory.EnumerateFiles(certificateDirectory, "*", SearchOption.AllDirectories)
                     .Any(IsSupportedCertificateFile))
        {
            missingResources.Add(
                @"webserver\apache\certificats (no .cer, .crt, .der, or .pem certificate found)");
        }

        if (hostsEntriesPath is null)
        {
            missingResources.Add(@"webserver\hosts*.txt");
        }

        if (missingResources.Count > 0)
        {
            MessageBox.Show(
                this,
                "Could not find these required onboarding resources under the selected folder:\n\n" +
                string.Join(Environment.NewLine, missingResources) +
                "\n\nSelect the folder that contains these resources.",
                "Folder not ready",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (hostsEntriesPath is null)
        {
            return;
        }

        try
        {
            HostsFileInstaller.CountValidEntries(hostsEntriesPath);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException)
        {
            MessageBox.Show(
                this,
                $"The hosts entry file could not be used:\n\n{exception.Message}",
                "Invalid hosts entries",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _selectedRblxHubPath = dialog.SelectedPath;
        _pathStepComplete = true;
        _certificatesStepComplete = false;
        _hostsStepComplete = false;
        RefreshPlaceIdFields();
        MarkStepComplete(_selectPathButton, _onboardingPathStatus, "Selected");
        _selectPathButton.Text = "Select Path";
        ResetStep(_installCertificatesButton, _certificateStatus, "Install");
        ResetStep(_addHostsButton, _hostsStatus, "Add Entries");
        _installCertificatesButton.Enabled = true;
        _addHostsButton.Enabled = true;
        UpdateFinishAvailability();
    }

    private void InstallCertificates(object? sender, EventArgs e)
    {
        if (_selectedRblxHubPath is null)
        {
            return;
        }

        string certificateDirectory = Path.Combine(
            _selectedRblxHubPath,
            CertificateDirectoryRelativePath);
        List<string> certificateFiles = Directory
            .EnumerateFiles(certificateDirectory, "*", SearchOption.AllDirectories)
            .Where(IsSupportedCertificateFile)
            .ToList();
        if (certificateFiles.Count == 0)
        {
            MessageBox.Show(
                this,
                "No supported certificate files were found in the selected folder.",
                "Certificates not found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var certificates = new List<X509Certificate2>();
        try
        {
            foreach (string file in certificateFiles)
            {
                certificates.Add(X509CertificateLoader.LoadCertificateFromFile(file));
            }

            string subjects = string.Join(
                Environment.NewLine,
                certificates.Take(8).Select(certificate => $"• {certificate.Subject}"));
            if (certificates.Count > 8)
            {
                subjects += $"{Environment.NewLine}• and {certificates.Count - 8} more";
            }

            DialogResult consent = MessageBox.Show(
                this,
                $"Install {certificates.Count} certificate(s) from the selected RBLXHUB folder into " +
                "Current User\\Trusted Root Certification Authorities? " +
                "Trusted root certificates can affect which sites Windows considers secure." +
                $"{Environment.NewLine}{Environment.NewLine}{subjects}",
                "Confirm certificate installation",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (consent != DialogResult.Yes)
            {
                return;
            }

            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            var existingThumbprints = store.Certificates
                .Cast<X509Certificate2>()
                .Select(certificate => certificate.Thumbprint)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int addedCount = 0;
            foreach (X509Certificate2 certificate in certificates)
            {
                if (existingThumbprints.Add(certificate.Thumbprint))
                {
                    store.Add(certificate);
                    addedCount++;
                }
            }

            _certificatesStepComplete = true;
            MarkStepComplete(
                _installCertificatesButton,
                _certificateStatus,
                addedCount == 0 ? "Already installed" : $"Installed {addedCount}");
            UpdateFinishAvailability();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            CryptographicException or
            ArgumentException)
        {
            MessageBox.Show(
                this,
                $"Certificate installation failed:\n\n{exception.Message}",
                "Certificate installation failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            foreach (X509Certificate2 certificate in certificates)
            {
                certificate.Dispose();
            }
        }
    }

    private void AddHostsEntries(object? sender, EventArgs e)
    {
        if (_selectedRblxHubPath is null)
        {
            return;
        }

        string? sourcePath = FindHostsEntriesFile(_selectedRblxHubPath);
        if (sourcePath is null)
        {
            MessageBox.Show(
                this,
                @"Could not find a hosts*.txt file directly in the selected folder's webserver directory.",
                "Hosts file not found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            int entryCount = HostsFileInstaller.CountValidEntries(sourcePath);
            DialogResult consent = MessageBox.Show(
                this,
                $"Add {entryCount} host mapping(s) from webserver\\{Path.GetFileName(sourcePath)} to the Windows hosts file? " +
                "This changes name resolution on this PC, creates a backup, and requests administrator approval.",
                "Confirm hosts-file update",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (consent != DialogResult.Yes)
            {
                return;
            }

            string result = HostsFileInstaller.InstallWithElevation(sourcePath);
            _hostsStepComplete = true;
            MarkStepComplete(_addHostsButton, _hostsStatus, "Added");
            UpdateFinishAvailability();
            MessageBox.Show(
                this,
                result,
                "Hosts-file update complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            MessageBox.Show(
                this,
                "Administrator approval was cancelled. The hosts-file step is still incomplete.",
                "Hosts-file update cancelled",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException or
            System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                this,
                $"The hosts-file update failed:\n\n{exception.Message}",
                "Hosts-file update failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void FinishOnboarding(object? sender, EventArgs e)
    {
        if (!_pathStepComplete ||
            !_certificatesStepComplete ||
            !_hostsStepComplete ||
            string.IsNullOrWhiteSpace(_selectedRblxHubPath))
        {
            UpdateFinishAvailability();
            return;
        }

        try
        {
            SaveOnboardingState(true, _selectedRblxHubPath);
            _onboardingCompleted = true;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            MessageBox.Show(
                this,
                $"Onboarding completion could not be saved, so setup will appear again next launch:\n\n{exception.Message}",
                "Could not save setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        if (_globalBanner is not null)
        {
            Controls.Add(_globalBanner);
            _globalBanner.Location = new Point(2, 205);
            _globalBanner.Size = new Size(973, 24);
        }

        foreach (Control control in _onboardingControls)
        {
            control.Visible = false;
            Controls.Remove(control);
            control.Dispose();
        }

        _onboardingControls.Clear();
        _onboardingActive = false;
        foreach (Button button in _navigationButtons)
        {
            button.Visible = true;
        }

        if (_globalBanner is not null)
        {
            _globalBanner.Visible = true;
        }

        _navigationButtons[0].PerformClick();
        _globalBanner?.BringToFront();
        Invalidate();
    }

    private void MarkStepComplete(Button button, Label status, string message)
    {
        button.Text = "Complete";
        button.BackColor = Color.FromArgb(35, 110, 66);
        button.FlatAppearance.MouseOverBackColor = button.BackColor;
        button.FlatAppearance.MouseDownBackColor = button.BackColor;
        status.Text = message;
        status.ForeColor = Color.FromArgb(125, 220, 155);
    }

    private void ResetStep(Button button, Label status, string buttonText)
    {
        button.Text = buttonText;
        button.BackColor = Color.FromArgb(53, 65, 194);
        button.FlatAppearance.MouseOverBackColor = button.BackColor;
        button.FlatAppearance.MouseDownBackColor = button.BackColor;
        status.Text = "Required";
        status.ForeColor = Color.FromArgb(175, 175, 175);
    }

    private void UpdateFinishAvailability()
    {
        _finishOnboardingButton.Enabled =
            _pathStepComplete && _certificatesStepComplete && _hostsStepComplete;
    }

    private static bool IsSupportedCertificateFile(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".cer" or ".crt" or ".der" or ".pem";
    }

    private static void SetTabControlsVisible(IReadOnlyList<Control> controls, bool visible)
    {
        foreach (Control control in controls)
        {
            control.Visible = visible;
        }
    }

    private sealed record OnboardingState(bool Completed, string RblxHubPath);

    private sealed class OnboardingOverlay : Panel
    {
        public OnboardingOverlay()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.OptimizedDoubleBuffer,
                true);
            BackColor = Color.Transparent;
        }
    }

    private sealed class OutlinedLogoText : Control
    {
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public required FontFamily FontFamily { get; init; }

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float FontSize { get; init; }

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public StringAlignment TextAlign { get; init; }

        public OutlinedLogoText()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.OptimizedDoubleBuffer,
                true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var format = new StringFormat
            {
                Alignment = TextAlign,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            };
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddString(
                Text,
                FontFamily,
                (int)FontStyle.Regular,
                FontSize,
                new RectangleF(0, 0, ClientSize.Width, ClientSize.Height),
                format);
            using var fill = new SolidBrush(Color.White);
            using var outline = new Pen(Color.FromArgb(255, 45, 0, 135), 2f)
            {
                LineJoin = System.Drawing.Drawing2D.LineJoin.Round
            };
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(outline, path);
        }
    }
}
