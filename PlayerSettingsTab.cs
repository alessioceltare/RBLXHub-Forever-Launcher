using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace RBLXHubLauncher;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, RoundedTextBox> _userJsonFields = [];
    private string? _userJsonPath;
    private Label _userJsonStatus = null!;
    private RoundedTextBox _proxyAddressField = null!;
    private RoundedTextBox _proxyPortField = null!;
    private Button _proxyButton = null!;
    private Label _proxyStatus = null!;
    private Process? _proxyProcess;
    private readonly List<Control> _placeIdControls = [];
    private readonly List<RoundedTextBox> _placeIdFields = [];
    private readonly List<string> _overflowPlaceIds = [];
    private Control? _placeSection;
    private Label? _placeIdStatus;

    private void AddPlayerSettingsPanel()
    {
        var layout = new TableLayoutPanel
        {
            Location = new Point(24, 239),
            Size = new Size(931, 300),
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddPlayerSettingsSection(layout, 0, "User Settings");
        AddPlayerSettingsSection(layout, 1, "Multiplayer");
        AddPlayerSettingsSection(layout, 2, "Place Impersonation");

        Control userSection = layout.GetControlFromPosition(0, 0)!;
        RoundedTextBox displayNameField = AddSettingsField(userSection, "Display Name", 8, 42, "");
        RoundedTextBox usernameField = AddSettingsField(userSection, "Username", 8, 100, "");
        RoundedTextBox userIdField = AddSettingsField(userSection, "User ID", 8, 158, "");
        _userJsonFields["displayName"] = displayNameField;
        _userJsonFields["username"] = usernameField;
        _userJsonFields["id"] = userIdField;
        foreach ((string key, RoundedTextBox textBox) in _userJsonFields)
        {
            textBox.Validated += (_, _) => SaveUserJsonField(key, textBox);
            textBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter)
                {
                    return;
                }

                SaveUserJsonField(key, textBox);
                e.SuppressKeyPress = true;
            };
        }

        var verifiedLabel = new Label
        {
            Text = "Verified Badge",
            Location = new Point(8, 220),
            Size = new Size(250, 20),
            Font = CreateInterfaceFont(13),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        };
        userSection.Controls.Add(verifiedLabel);

        Button verifiedToggle = AddHostingButton(
            userSection,
            "ON",
            new Point(8, 244),
            new Size(250, 40),
            13);
        verifiedToggle.Click += (_, _) =>
        {
            bool enabled = verifiedToggle.Text == "ON";
            verifiedToggle.Text = enabled ? "OFF" : "ON";
            verifiedToggle.BackColor = enabled
                ? Color.FromArgb(64, 64, 64)
                : Color.FromArgb(53, 65, 194);
            verifiedToggle.FlatAppearance.MouseOverBackColor = verifiedToggle.BackColor;
            verifiedToggle.FlatAppearance.MouseDownBackColor = verifiedToggle.BackColor;
            SaveUserJsonBadge(verifiedToggle.Text == "ON");
        };

        _userJsonStatus = new Label
        {
            Text = "Loading user.json...",
            Location = new Point(8, 288),
            Size = new Size(875, 22),
            Font = CreateInterfaceFont(9),
            ForeColor = Color.FromArgb(175, 175, 175),
            AutoEllipsis = true
        };
        userSection.Controls.Add(_userJsonStatus);
        LoadUserJsonSettings(verifiedToggle);

        Control multiplayerSection = layout.GetControlFromPosition(1, 0)!;
        _proxyAddressField = AddSettingsField(multiplayerSection, "Address", 8, 42, "");
        _proxyPortField = AddSettingsField(multiplayerSection, "Port", 8, 100, "");
        _proxyButton = AddHostingButton(
            multiplayerSection,
            "Start Proxy",
            new Point(8, 166),
            new Size(250, 40),
            13);
        _proxyButton.Click += ToggleProxy;
        _proxyStatus = new Label
        {
            Text = string.Empty,
            Location = new Point(8, 212),
            Size = new Size(270, 48),
            Font = CreateInterfaceFont(9),
            ForeColor = Color.FromArgb(175, 175, 175),
            AutoEllipsis = true
        };
        multiplayerSection.Controls.Add(_proxyStatus);

        _placeSection = layout.GetControlFromPosition(2, 0)!;
        RefreshPlaceIdFields();

        Controls.Add(layout);
        _playerSettingsControls.Add(layout);
        layout.Visible = false;
    }

    private void RefreshPlaceIdFields()
    {
        if (_placeSection is null)
        {
            return;
        }

        foreach (Control control in _placeIdControls)
        {
            _placeSection.Controls.Remove(control);
            control.Dispose();
        }
        _placeIdControls.Clear();
        _placeIdFields.Clear();
        _overflowPlaceIds.Clear();
        _placeIdStatus = null;

        int? detectedPlaceIdCount = DetectSupportedPlaceIdCount();
        int placeIdCount = detectedPlaceIdCount ?? 1;
        int placeIdSpacing = placeIdCount == 1 ? 58 : 54;
        for (int index = 0; index < placeIdCount; index++)
        {
            AddPlaceIdField(
                _placeSection,
                placeIdCount == 1 ? "Place ID" : $"Place ID {index + 1}",
                8,
                42 + index * placeIdSpacing,
                "");
        }

        _placeIdStatus = new Label
        {
            Text = detectedPlaceIdCount is null
                ? "Could not detect Place ID support in asset.php."
                : string.Empty,
            Location = new Point(8, 260),
            Size = new Size(275, 30),
            Font = CreateInterfaceFont(8),
            ForeColor = detectedPlaceIdCount is null
                ? Color.FromArgb(255, 170, 110)
                : Color.FromArgb(125, 220, 155),
            AutoEllipsis = true
        };
        _placeSection.Controls.Add(_placeIdStatus);
        _placeIdControls.Add(_placeIdStatus);

        LoadPlaceIds();
    }

    private void AddPlaceIdField(
        Control parent,
        string label,
        int left,
        int top,
        string placeholder)
    {
        int firstControlIndex = parent.Controls.Count;
        RoundedTextBox field = AddSettingsField(parent, label, left, top, placeholder);
        Control fieldLabel = parent.Controls[firstControlIndex];
        _placeIdControls.Add(fieldLabel);
        _placeIdControls.Add(field);
        _placeIdFields.Add(field);
        field.Validated += (_, _) => SavePlaceIds();
        field.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            SavePlaceIds();
            e.SuppressKeyPress = true;
        };
    }

    private int? DetectSupportedPlaceIdCount()
    {
        string? assetPath = GetSelectedAssetPhpPath();
        if (assetPath is null || !File.Exists(assetPath))
        {
            return null;
        }

        string phpSource;
        try
        {
            phpSource = File.ReadAllText(assetPath);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                $"Could not inspect asset.php, so only one Place ID field will be shown:\n\n{exception.Message}",
                "Place ID support detection",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return null;
        }
        string sourceWithoutComments = MaskPhpComments(phpSource);

        bool supportsMultiple = Regex.IsMatch(
            sourceWithoutComments,
            @"(?m)^\s*\$placeIdsToImpersonate\s*=");
        bool supportsSingle = Regex.IsMatch(
            sourceWithoutComments,
            @"(?m)^\s*\$placeIdToImpersonate\s*=");

        if (supportsSingle && !supportsMultiple)
        {
            return 1;
        }

        return supportsMultiple ? 4 : null;
    }

    private string? GetSelectedAssetPhpPath()
    {
        return _selectedRblxHubPath is null
            ? null
            : Path.Combine(_selectedRblxHubPath, "webserver", "apache", "www", "v1", "asset.php");
    }

    private void LoadPlaceIds()
    {
        string? assetPath = GetSelectedAssetPhpPath();
        if (assetPath is null || !File.Exists(assetPath))
        {
            SetPlaceIdStatus("asset.php not found in the selected RBLXHUB folder.", isError: true);
            return;
        }

        try
        {
            string source = File.ReadAllText(assetPath);
            List<string> placeIds = ReadPlaceIds(source);
            for (int index = 0; index < _placeIdFields.Count; index++)
            {
                _placeIdFields[index].Text = index < placeIds.Count
                    ? placeIds[index]
                    : string.Empty;
            }
            _overflowPlaceIds.AddRange(placeIds.Skip(_placeIdFields.Count));

            SetPlaceIdStatus(string.Empty, isError: false);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            SetPlaceIdStatus($"Could not load Place IDs: {exception.Message}", isError: true);
        }
    }

    private void SavePlaceIds()
    {
        string? assetPath = GetSelectedAssetPhpPath();
        if (assetPath is null || !File.Exists(assetPath))
        {
            SetPlaceIdStatus("asset.php not found in the selected RBLXHUB folder.", isError: true);
            return;
        }

        string[] placeIds = _placeIdFields
            .Select(field => field.Text.Trim())
            .Concat(_overflowPlaceIds)
            .ToArray();
        foreach (string placeId in placeIds)
        {
            if (placeId.Length > 0 && !Regex.IsMatch(placeId, @"^[0-9]+$"))
            {
                SetPlaceIdStatus("Place IDs must contain digits only.", isError: true);
                return;
            }
        }

        try
        {
            string source = File.ReadAllText(assetPath);
            string updatedSource = WritePlaceIds(source, placeIds);
            string temporaryPath = assetPath + ".launcher.tmp";
            try
            {
                File.WriteAllText(temporaryPath, updatedSource);
                File.Move(temporaryPath, assetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            SetPlaceIdStatus("Place IDs saved to asset.php.", isError: false);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            SetPlaceIdStatus($"Could not save Place IDs: {exception.Message}", isError: true);
        }
    }

    private static List<string> ReadPlaceIds(string source)
    {
        string searchableSource = MaskPhpComments(source);
        Match multiMatch = Regex.Match(
            searchableSource,
            @"\$placeIdsToImpersonate\s*=\s*array\s*\((?<values>.*?)\)\s*;",
            RegexOptions.Singleline);
        if (multiMatch.Success)
        {
            string values = source.Substring(
                multiMatch.Groups["values"].Index,
                multiMatch.Groups["values"].Length);
            values = MaskPhpComments(values);
            return Regex.Matches(values, @"['""](?<id>[0-9]+)['""]")
                .Cast<Match>()
                .Select(match => match.Groups["id"].Value)
                .ToList();
        }

        Match singleMatch = Regex.Match(
            searchableSource,
            @"\$placeIdToImpersonate\s*=\s*['""](?<id>[0-9]+)['""]\s*;");
        if (singleMatch.Success)
        {
            Group id = singleMatch.Groups["id"];
            return [source.Substring(id.Index, id.Length)];
        }

        throw new InvalidDataException("No supported Place ID setting was found in asset.php.");
    }

    private static string WritePlaceIds(string source, IReadOnlyList<string> placeIds)
    {
        string searchableSource = MaskPhpComments(source);
        Match multiMatch = Regex.Match(
            searchableSource,
            @"(?<prefix>\$placeIdsToImpersonate\s*=\s*array\s*\()(?<values>.*?)(?<suffix>\)\s*;)",
            RegexOptions.Singleline);
        if (multiMatch.Success)
        {
            string newValues = Environment.NewLine + string.Join(
                Environment.NewLine,
                placeIds.Select(placeId =>
                    $"    \"{(placeId.Length == 0 ? "0" : placeId)}\","));
            Group valuesGroup = multiMatch.Groups["values"];
            return source[..valuesGroup.Index] +
                   newValues +
                   source[(valuesGroup.Index + valuesGroup.Length)..];
        }

        Match singleMatch = Regex.Match(
            searchableSource,
            @"\$placeIdToImpersonate\s*=\s*['""](?<id>[0-9]+)['""]\s*;");
        if (singleMatch.Success && placeIds.Count > 0)
        {
            string value = placeIds[0].Length == 0 ? "0" : placeIds[0];
            return source[..singleMatch.Groups["id"].Index] +
                   value +
                   source[(singleMatch.Groups["id"].Index + singleMatch.Groups["id"].Length)..];
        }

        throw new InvalidDataException("No supported Place ID setting was found in asset.php.");
    }

    private void SetPlaceIdStatus(string message, bool isError)
    {
        if (_placeIdStatus is null)
        {
            return;
        }

        _placeIdStatus.Text = message;
        _placeIdStatus.ForeColor = isError
            ? Color.FromArgb(255, 125, 125)
            : Color.FromArgb(125, 220, 155);
    }

    private static string MaskPhpComments(string source)
    {
        return Regex.Replace(
            source,
            @"/\*.*?\*/|//[^\r\n]*|#[^\r\n]*",
            match => new string(
                match.Value.Select(character => character is '\r' or '\n' ? character : ' ').ToArray()),
            RegexOptions.Singleline | RegexOptions.Multiline);
    }

    private void AddPlayerSettingsSection(TableLayoutPanel layout, int column, string title)
    {
        var section = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 8, 0)
        };
        layout.Controls.Add(section, column, 0);

        var heading = new Label
        {
            Text = title,
            Location = new Point(8, 8),
            Size = new Size(section.Width - 16, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = CreateInterfaceFont(18),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        };
        section.Controls.Add(heading);
    }

    private RoundedTextBox AddSettingsField(
        Control parent,
        string label,
        int left,
        int top,
        string placeholder)
    {
        var fieldLabel = new Label
        {
            Text = label,
            Location = new Point(left, top),
            Size = new Size(250, 18),
            Font = CreateInterfaceFont(13),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var textBox = new RoundedTextBox
        {
            AutoSize = false,
            Location = new Point(left, top + 20),
            Size = new Size(250, 35),
            Font = CreateInterfaceFont(16),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(64, 64, 64),
            BorderStyle = BorderStyle.None,
            PlaceholderText = placeholder,
            CornerRadius = 6
        };
        parent.Controls.Add(fieldLabel);
        parent.Controls.Add(textBox);
        return textBox;
    }

    private void LoadUserJsonSettings(Button verifiedToggle)
    {
        _userJsonPath = _selectedRblxHubPath is null
            ? null
            : Path.Combine(
                _selectedRblxHubPath,
                "webserver",
                "apache",
                "www",
                "config",
                "user.json");

        if (_userJsonPath is null || !File.Exists(_userJsonPath))
        {
            SetUserJsonStatus(
                "user.json not found. Complete onboarding and select the correct RBLXHUB folder.",
                isError: true);
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(_userJsonPath));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("user.json must contain a JSON object.");
            }

            _userJsonFields["displayName"].Text =
                root.TryGetProperty("displayName", out JsonElement displayName) &&
                displayName.ValueKind == JsonValueKind.String
                    ? displayName.GetString() ?? string.Empty
                    : string.Empty;
            _userJsonFields["username"].Text =
                root.TryGetProperty("username", out JsonElement username) &&
                username.ValueKind == JsonValueKind.String
                    ? username.GetString() ?? string.Empty
                    : string.Empty;
            _userJsonFields["id"].Text =
                root.TryGetProperty("id", out JsonElement id) &&
                id.ValueKind is JsonValueKind.String or JsonValueKind.Number
                    ? id.ToString()
                    : string.Empty;

            bool hasVerifiedBadge =
                root.TryGetProperty("hasVerifiedBadge", out JsonElement badge) &&
                badge.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                badge.GetBoolean();
            SetBadgeToggle(verifiedToggle, hasVerifiedBadge);
            SetUserJsonStatus(string.Empty, isError: false);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            SetUserJsonStatus($"Could not load user.json: {exception.Message}", isError: true);
        }
    }

    private void SaveUserJsonField(string propertyName, TextBox textBox)
    {
        if (!TryLoadUserJson(out JsonObject? user))
        {
            return;
        }

        if (propertyName == "id" &&
            user[propertyName] is JsonValue currentId &&
            currentId.TryGetValue<long>(out _) &&
            !long.TryParse(textBox.Text, out _))
        {
            SetUserJsonStatus("User ID must be a whole number.", isError: true);
            return;
        }

        user[propertyName] = propertyName == "id" &&
            user[propertyName] is JsonValue existingId &&
            existingId.TryGetValue<long>(out _) &&
            long.TryParse(textBox.Text, out long numericId)
                ? JsonValue.Create(numericId)
                : JsonValue.Create(textBox.Text);

        SaveUserJson(user);
    }

    private void SaveUserJsonBadge(bool enabled)
    {
        if (!TryLoadUserJson(out JsonObject? user))
        {
            return;
        }

        user["hasVerifiedBadge"] = enabled;
        SaveUserJson(user);
    }

    private bool TryLoadUserJson(out JsonObject user)
    {
        user = null!;
        if (_userJsonPath is null)
        {
            SetUserJsonStatus("No RBLXHUB folder is configured.", isError: true);
            return false;
        }

        try
        {
            JsonNode? parsed = JsonNode.Parse(File.ReadAllText(_userJsonPath));
            if (parsed is not JsonObject jsonObject)
            {
                SetUserJsonStatus("user.json must contain a JSON object.", isError: true);
                return false;
            }

            user = jsonObject;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            SetUserJsonStatus($"Could not read user.json: {exception.Message}", isError: true);
            return false;
        }
    }

    private void SaveUserJson(JsonObject user)
    {
        if (_userJsonPath is null)
        {
            SetUserJsonStatus("No RBLXHUB folder is configured.", isError: true);
            return;
        }

        string temporaryPath = _userJsonPath + ".tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                user.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, _userJsonPath, overwrite: true);
            SetUserJsonStatus($"Saved changes to {_userJsonPath}", isError: false);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            SetUserJsonStatus($"Could not save user.json: {exception.Message}", isError: true);
        }
    }

    private void SetUserJsonStatus(string message, bool isError)
    {
        _userJsonStatus.Text = message;
        _userJsonStatus.ForeColor = isError
            ? Color.FromArgb(255, 125, 125)
            : Color.FromArgb(125, 220, 155);
    }

    private static void SetBadgeToggle(Button toggle, bool enabled)
    {
        toggle.Text = enabled ? "ON" : "OFF";
        toggle.BackColor = enabled
            ? Color.FromArgb(53, 65, 194)
            : Color.FromArgb(64, 64, 64);
        toggle.FlatAppearance.MouseOverBackColor = toggle.BackColor;
        toggle.FlatAppearance.MouseDownBackColor = toggle.BackColor;
    }

    private void ToggleProxy(object? sender, EventArgs e)
    {
        if (_proxyProcess is { HasExited: false })
        {
            StopProxy();
            return;
        }

        StartProxy();
    }

    private void StartProxy()
    {
        if (string.IsNullOrWhiteSpace(_selectedRblxHubPath))
        {
            SetProxyStatus("Complete onboarding to select the proxy script folder.", isError: true);
            return;
        }

        string scriptPath = Path.Combine(_selectedRblxHubPath, "2026E", "proxy.py");
        if (!File.Exists(scriptPath))
        {
            SetProxyStatus($"Proxy script not found: {scriptPath}", isError: true);
            return;
        }

        string remoteAddress = _proxyAddressField.Text.Trim();
        if (string.IsNullOrWhiteSpace(remoteAddress) ||
            remoteAddress.Any(char.IsWhiteSpace) ||
            remoteAddress.Contains(';') ||
            remoteAddress.Contains('&') ||
            remoteAddress.Contains('|'))
        {
            SetProxyStatus("Enter a valid remote address.", isError: true);
            return;
        }

        if (!int.TryParse(_proxyPortField.Text.Trim(), out int remotePort) ||
            remotePort is < 1 or > 65535)
        {
            SetProxyStatus("Enter a port from 1 to 65535.", isError: true);
            return;
        }

        string? pythonPath = FindPythonExecutable();
        if (pythonPath is null)
        {
            SetProxyStatus("Python was not found. Install Python 3 and add it to PATH.", isError: true);
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath)!
        };
        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(remoteAddress);
        startInfo.ArgumentList.Add(remotePort.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += ProxyOutputReceived;
        process.ErrorDataReceived += ProxyErrorReceived;
        process.Exited += ProxyProcessExited;

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                SetProxyStatus("Python could not be started.", isError: true);
                return;
            }

            _proxyProcess = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _proxyButton.Text = "Stop Proxy";
            SetProxyStatus($"Starting proxy to {remoteAddress}:{remotePort}...", isError: false);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            IOException)
        {
            process.Dispose();
            SetProxyStatus($"Could not start proxy: {exception.Message}", isError: true);
        }
    }

    private static string? FindPythonExecutable()
    {
        string[] pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string directory in pathDirectories)
        {
            if (directory.Contains(@"\Microsoft\WindowsApps", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string candidate = Path.Combine(directory, "python.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private void ProxyOutputReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null ||
            (!e.Data.StartsWith("[proxy] Ready", StringComparison.Ordinal) &&
             !e.Data.StartsWith("[proxy] UDP tunnel:", StringComparison.Ordinal)))
        {
            return;
        }

        UpdateProxyUi(() => SetProxyStatus(e.Data, isError: false));
    }

    private void ProxyErrorReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
        {
            UpdateProxyUi(() => SetProxyStatus($"Proxy error: {e.Data}", isError: true));
        }
    }

    private void ProxyProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process process)
        {
            return;
        }

        int exitCode;
        try
        {
            exitCode = process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            exitCode = -1;
        }

        UpdateProxyUi(() =>
        {
            if (!ReferenceEquals(_proxyProcess, process))
            {
                return;
            }

            _proxyProcess = null;
            _proxyButton.Text = "Start Proxy";
            SetProxyStatus(
                exitCode == 0
                    ? "Proxy stopped."
                    : $"Proxy exited with code {exitCode}. Check the address, port, and proxy errors.",
                isError: exitCode != 0);
            process.Dispose();
        });
    }

    private void StopProxy()
    {
        Process? process = _proxyProcess;
        _proxyProcess = null;
        if (process is null)
        {
            _proxyButton.Text = "Start Proxy";
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            SetProxyStatus($"Could not stop proxy: {exception.Message}", isError: true);
        }

        process.Dispose();
        _proxyButton.Text = "Start Proxy";
        SetProxyStatus("Proxy stopped.", isError: false);
    }

    private void SetProxyStatus(string message, bool isError)
    {
        _proxyStatus.Text = message;
        _proxyStatus.ForeColor = isError
            ? Color.FromArgb(255, 125, 125)
            : Color.FromArgb(175, 175, 175);
    }

    private void UpdateProxyUi(Action update)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(update);
            }
            catch (InvalidOperationException)
            {
                // The form may close between the handle check and dispatch.
            }
        }
        else
        {
            update();
        }
    }

    private sealed class RoundedTextBox : TextBox
    {
        private int _cornerRadius;

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
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
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
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
}
