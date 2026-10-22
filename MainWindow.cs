using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RBLXHubLauncher;

public sealed partial class MainWindow : Form
{
    private const int DarkTitleBarAttribute = 20;
    private readonly Image? _backgroundImage;
    private readonly Image _logoIcon;
    private readonly Icon _applicationIcon;
    private readonly PrivateFontCollection _logoFontCollection = new();
    private readonly FontFamily _logoFontFamily;
    private readonly PrivateFontCollection _navigationFontCollection = new();
    private readonly FontFamily _navigationFontFamily;
    private readonly Font _navigationFont;
    private readonly Font _bannerFont;
    private readonly List<Button> _navigationButtons = [];
    private readonly List<Control> _playerSettingsControls = [];
    private readonly List<Control> _launcherSettingsControls = [];
    private Label? _globalBanner;
    private bool _onboardingActive;
    private bool _onboardingCompleted;
    private static readonly string[] BannerMessages =
    [
        "hey its me its verity",
		"when is vc coming",
		"RBLXHUB FOREVER",
		"dystamyurm made this launcher",
		"gubby rcc",
		"verity rcc",
		"rbolock.tk",
		"idk what to add here",
		"verity funk",
		"fuck you rockstar games, fuck your game fuck your name fuck your millions fuck your billions",
		"ask me anything!",
		"meow",
		"hihi welcome thanks for using this launcher",
		"wanna host?",
		"good whatever time it is for you.",
		"kanye is cool",
		"hi",
		"hey",
		"wassap",
		"i use arch btw",
		"# arch install type shi",
		"you ever just wanna sudo rm -rf --no-preserve-root /?",
		"fuck roblox",
		"rblxhub forever is peak.",
		"thanks for using my launcher.",
		"rblxhub forever > roblox",
		"you're*",
		"im glad you like using my launcher!",
		"something something host, something something portwarp",
		"Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua."
	];
    private static string BannerIndexPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RBLXHubLauncher",
        "banner-index.txt");

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    public MainWindow()
    {
        LoadEmbeddedLogoFont();
        _logoFontFamily = _logoFontCollection.Families[0];
        LoadEmbeddedNavigationFont();
        _navigationFontFamily = _navigationFontCollection.Families[0];
        _navigationFont = new Font(_navigationFontFamily, 13, FontStyle.Regular);
        _bannerFont = new Font(_navigationFontFamily, 12, FontStyle.Regular);
        _logoIcon = LoadEmbeddedLogoIcon();
        _applicationIcon = LoadEmbeddedApplicationIcon();

        Text = "RBLXHUB Forever Launcher";
        Icon = _applicationIcon;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(979, 573);
        BackColor = Color.FromArgb(23, 21, 21);
        ForeColor = Color.FromArgb(244, 244, 245);
        DoubleBuffered = true;

        _backgroundImage = LoadEmbeddedBannerImage();

        LoadSavedOnboardingState();
        LoadLauncherPreferences();
        AddNavigationButtons();
        AddHostingPanel();
        AddPlayerSettingsPanel();
        AddLauncherSettingsPanel();
        AddGlobalBanner();
        InitializeOnboarding();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        Color backgroundColor = BackColor;
        graphics.Clear(backgroundColor);

        Rectangle bannerBounds = new(0, 0, ClientSize.Width, 230);
        GraphicsState state = graphics.Save();
        graphics.SetClip(bannerBounds);

        if (_backgroundImage is not null)
        {
            float scale = (float)ClientSize.Width / _backgroundImage.Width;
            float imageHeight = _backgroundImage.Height * scale;
            const float imageTop = -175f;
            graphics.DrawImage(
                _backgroundImage,
                new RectangleF(0, imageTop, ClientSize.Width, imageHeight));
        }
        else
        {
            using var placeholderBrush = new SolidBrush(Color.FromArgb(35, 33, 33));
            graphics.FillRectangle(placeholderBrush, bannerBounds);

            const string message = "Add download.jpg beside the app to show this image";
            using var messageFont = new Font("Segoe UI", 10);
            using var messageBrush = new SolidBrush(Color.FromArgb(170, 170, 170));
            SizeF messageSize = graphics.MeasureString(message, messageFont);
            graphics.DrawString(
                message,
                messageFont,
                messageBrush,
                (bannerBounds.Width - messageSize.Width) / 2,
                (bannerBounds.Height - messageSize.Height) / 2);
        }

        const int gradientTop = 23;
        int gradientBottom = bannerBounds.Height;
        for (int y = gradientTop; y < gradientBottom; y++)
        {
            int alpha = (y - gradientTop) * 255 / (gradientBottom - gradientTop - 1);
            using var fadeBrush = new SolidBrush(Color.FromArgb(
                alpha,
                backgroundColor.R,
                backgroundColor.G,
                backgroundColor.B));
            graphics.FillRectangle(fadeBrush, 0, y, ClientSize.Width, 1);
        }

        graphics.Restore(state);

        if (!_onboardingActive)
        {
            graphics.DrawImage(_logoIcon, new Rectangle(0, 0, 120, 120));

            using var logoPath = new GraphicsPath();
            logoPath.AddString(
                "Launcher",
                _logoFontFamily,
                (int)FontStyle.Regular,
                36,
                new Point(120, 50),
                StringFormat.GenericDefault);
            using var logoOutline = new Pen(Color.FromArgb(255, 45, 0, 135), 1.6f)
            {
                LineJoin = LineJoin.Round
            };
            using var logoFill = new SolidBrush(Color.White);
            graphics.FillPath(logoFill, logoPath);
            graphics.DrawPath(logoOutline, logoPath);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopProxy();
            _webServerMonitor?.Stop();
            _webServerMonitor?.Dispose();
            _webServerStartCommand?.Dispose();
            _hostingProcess?.Dispose();
            _backgroundImage?.Dispose();
            _logoIcon.Dispose();
            _applicationIcon.Dispose();
            _logoFontFamily.Dispose();
            _logoFontCollection.Dispose();
            _navigationFont.Dispose();
            _bannerFont.Dispose();
            _navigationFontFamily.Dispose();
            _navigationFontCollection.Dispose();
            foreach (Font font in _interfaceFonts)
            {
                font.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        int enabled = 1;
        DwmSetWindowAttribute(
            Handle,
            DarkTitleBarAttribute,
            ref enabled,
            sizeof(int));
    }

    private void LoadEmbeddedLogoFont()
    {
        using Stream fontStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("RBLXHubLauncher.BobloxClassic.ttf")
            ?? throw new InvalidOperationException("The embedded BobloxClassic font was not found.");
        byte[] fontBytes = new byte[fontStream.Length];
        fontStream.ReadExactly(fontBytes);

        IntPtr fontMemory = Marshal.AllocHGlobal(fontBytes.Length);
        try
        {
            Marshal.Copy(fontBytes, 0, fontMemory, fontBytes.Length);
            _logoFontCollection.AddMemoryFont(fontMemory, fontBytes.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(fontMemory);
        }

        if (_logoFontCollection.Families.Length == 0)
        {
            throw new InvalidOperationException("The embedded BobloxClassic font could not be loaded.");
        }
    }

    private static Image LoadEmbeddedLogoIcon()
    {
        using Stream iconStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("RBLXHubLauncher.default-icon.png")
            ?? throw new InvalidOperationException("The embedded launcher icon was not found.");
        using Image source = Image.FromStream(iconStream);
        return new Bitmap(source);
    }

    private static Icon LoadEmbeddedApplicationIcon()
    {
        using Stream iconStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("RBLXHubLauncher.icon.ico")
            ?? throw new InvalidOperationException("The embedded application icon was not found.");
        using var memoryStream = new MemoryStream();
        iconStream.CopyTo(memoryStream);
        memoryStream.Position = 0;
        return new Icon(memoryStream);
    }

    private static Image LoadEmbeddedBannerImage()
    {
        using Stream imageStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("RBLXHubLauncher.download.jpg")
            ?? throw new InvalidOperationException("The embedded banner image was not found.");
        using Image source = Image.FromStream(imageStream);
        return new Bitmap(source);
    }

    private void LoadEmbeddedNavigationFont()
    {
        using Stream fontStream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("RBLXHubLauncher.Gotham-Medium.otf")
            ?? throw new InvalidOperationException("The embedded Gotham font was not found.");
        byte[] fontBytes = new byte[fontStream.Length];
        fontStream.ReadExactly(fontBytes);

        IntPtr fontMemory = Marshal.AllocHGlobal(fontBytes.Length);
        try
        {
            Marshal.Copy(fontBytes, 0, fontMemory, fontBytes.Length);
            _navigationFontCollection.AddMemoryFont(fontMemory, fontBytes.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(fontMemory);
        }

        if (_navigationFontCollection.Families.Length == 0)
        {
            throw new InvalidOperationException("The embedded Gotham font could not be loaded.");
        }
    }

    private void AddNavigationButtons()
    {
        string[] labels = ["Hosting", "Player Settings", "Launcher Settings"];
        const int firstButtonLeft = 400;
        const int buttonGapPixels = 12;
        Size buttonSize = new(180, 50);

        for (int index = 0; index < labels.Length; index++)
        {
            var button = new Button
            {
                Text = labels[index],
                Location = new Point(
                    firstButtonLeft + index * (buttonSize.Width + buttonGapPixels),
                    50),
                Size = buttonSize,
                Region = CreateRoundedRegion(new Rectangle(Point.Empty, buttonSize), 5),
                Font = _navigationFont,
                ForeColor = Color.White,
                BackColor = index == 0
                    ? Color.FromArgb(21, 29, 126)
                    : Color.FromArgb(53, 65, 194),
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleCenter,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            button.SizeChanged += (_, _) =>
            {
                Region previousRegion = button.Region;
                button.Region = CreateRoundedRegion(
                    new Rectangle(Point.Empty, button.Size),
                    Math.Max(4, (int)Math.Round(5f * DeviceDpi / 96f)));
                previousRegion.Dispose();
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = button.BackColor;
            button.FlatAppearance.MouseDownBackColor = button.BackColor;
            button.Click += NavigationButton_Click;
            _navigationButtons.Add(button);
            Controls.Add(button);
        }
    }

    private void AddGlobalBanner()
    {
        _globalBanner = new Label
        {
            Text = GetNextBannerMessage(),
            Location = new Point(2, 205),
            Size = new Size(973, 24),
            Font = _bannerFont,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(43, 43, 43),
            TextAlign = ContentAlignment.MiddleCenter,
            UseCompatibleTextRendering = true
        };
        Controls.Add(_globalBanner);
        _globalBanner.BringToFront();
    }

    private static string GetNextBannerMessage()
    {
        try
        {
            if (BannerMessages.Length == 0)
            {
                return "hey its me its verity";
            }

            int previousIndex = -1;
            if (File.Exists(BannerIndexPath) &&
                int.TryParse(File.ReadAllText(BannerIndexPath), out int parsedIndex))
            {
                previousIndex = parsedIndex;
            }

            if (previousIndex < -1 || previousIndex >= BannerMessages.Length)
            {
                previousIndex = -1;
            }

            int nextIndex = (previousIndex + 1) % BannerMessages.Length;
            string? stateDirectory = Path.GetDirectoryName(BannerIndexPath);
            if (stateDirectory is not null)
            {
                Directory.CreateDirectory(stateDirectory);
            }

            File.WriteAllText(BannerIndexPath, nextIndex.ToString());
            return BannerMessages[nextIndex];
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"Banner messages could not be loaded or rotated.\n\n{exception.Message}",
                "Banner messages",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return "hey its me its verity";
        }
    }

    private static Region CreateRoundedRegion(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return new Region(path);
    }

    private void NavigationButton_Click(object? sender, EventArgs e)
    {
        if (sender is not Button selectedButton)
        {
            return;
        }

        foreach (Button button in _navigationButtons)
        {
            button.BackColor = button == selectedButton
                ? Color.FromArgb(21, 29, 126)
                : Color.FromArgb(53, 65, 194);
            button.FlatAppearance.MouseOverBackColor = button.BackColor;
            button.FlatAppearance.MouseDownBackColor = button.BackColor;
        }

        foreach (Control control in _hostingControls)
        {
            control.Visible = _navigationButtons[0] == selectedButton;
        }

        foreach (Control control in _playerSettingsControls)
        {
            control.Visible = _navigationButtons[1] == selectedButton;
        }

        foreach (Control control in _launcherSettingsControls)
        {
            control.Visible = _navigationButtons[2] == selectedButton;
        }
    }
}