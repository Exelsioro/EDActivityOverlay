using System.Drawing;
using System.Windows.Forms;

namespace MockTargetApp;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        HarnessOptions options = HarnessOptions.Parse(args);
        Application.Run(new ResolutionHarnessForm(options));
    }
}

internal sealed record HarnessOptions(
    Size InitialSize,
    Point? InitialPosition,
    bool Borderless)
{
    public static HarnessOptions Parse(string[] args)
    {
        Size size = TargetResolutionCatalog.Resolve("fhd").Size;
        Point? position = null;
        bool borderless = false;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];

            if (TryReadValue(args, ref index, argument, "--preset", out string? preset))
            {
                size = TargetResolutionCatalog.Resolve(preset).Size;
                continue;
            }

            if (TryReadValue(args, ref index, argument, "--size", out string? customSize))
            {
                if (!TargetResolutionCatalog.TryParseSize(customSize, out size))
                {
                    throw new ArgumentException(
                        $"Invalid --size value '{customSize}'. Expected WIDTHxHEIGHT.");
                }

                continue;
            }

            if (TryReadValue(args, ref index, argument, "--position", out string? customPosition))
            {
                if (!TargetResolutionCatalog.TryParsePosition(customPosition, out Point parsed))
                {
                    throw new ArgumentException(
                        $"Invalid --position value '{customPosition}'. Expected X,Y.");
                }

                position = parsed;
                continue;
            }

            if (argument.Equals("--borderless", StringComparison.OrdinalIgnoreCase))
            {
                borderless = true;
                continue;
            }

            throw new ArgumentException(
                $"Unknown argument '{argument}'. Supported: --preset, --size, --position, --borderless.");
        }

        return new HarnessOptions(size, position, borderless);
    }

    private static bool TryReadValue(
        string[] args,
        ref int index,
        string argument,
        string name,
        out string? value)
    {
        value = null;
        string prefix = name + "=";

        if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = argument[prefix.Length..];
            return true;
        }

        if (!argument.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value after {name}.");
        }

        value = args[++index];
        return true;
    }
}

internal sealed class ResolutionHarnessForm : Form
{
    private readonly Label geometryLabel;
    private int currentPresetIndex;

    public ResolutionHarnessForm(HarnessOptions options)
    {
        Text = "Mock Target Application";
        StartPosition = FormStartPosition.Manual;
        Location = options.InitialPosition ?? new Point(20, 20);
        FormBorderStyle = options.Borderless
            ? FormBorderStyle.None
            : FormBorderStyle.Sizable;
        MinimumSize = new Size(640, 480);
        Size = options.InitialSize;
        KeyPreview = true;
        BackColor = Color.FromArgb(10, 10, 14);
        ForeColor = Color.White;

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(8, 7, 8, 4),
            AutoScroll = true,
            WrapContents = false,
            BackColor = Color.FromArgb(28, 28, 34)
        };

        foreach (TargetResolutionPreset preset in TargetResolutionCatalog.All)
        {
            var button = new Button
            {
                Text = $"{preset.Key}  {preset.Label}",
                AutoSize = true,
                Height = 30,
                Tag = preset
            };

            button.Click += (_, _) => ApplyPreset(preset);
            controls.Controls.Add(button);
        }

        geometryLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 92,
            Padding = new Padding(18, 18, 18, 6),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Consolas", 12, FontStyle.Bold),
            ForeColor = Color.LightGreen,
            BackColor = Color.Transparent
        };

        var explanationLabel = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 12),
            ForeColor = Color.Gainsboro,
            BackColor = Color.Transparent,
            Text =
                "EDActivityOverlay RESOLUTION TARGET\r\n\r\n"
                + "1-6 change preset, F11 cycles presets.\r\n"
                + "Ctrl+Arrow moves the target by 100 px.\r\n\r\n"
                + "Large presets on an FHD monitor test resize/clamping only; "
                + "they do not emulate real 4K DPI."
        };

        var mainPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(8, 8, 12)
        };

        mainPanel.Controls.Add(explanationLabel);
        mainPanel.Controls.Add(geometryLabel);
        Controls.Add(mainPanel);
        Controls.Add(controls);

        SizeChanged += (_, _) => UpdateGeometry();
        Move += (_, _) => UpdateGeometry();
        Shown += (_, _) => UpdateGeometry();
        KeyDown += ResolutionHarnessForm_KeyDown;

        currentPresetIndex = FindNearestPresetIndex(options.InitialSize);
    }

    private void ResolutionHarnessForm_KeyDown(object? sender, KeyEventArgs e)
    {
        int requested = e.KeyCode switch
        {
            Keys.D1 or Keys.NumPad1 => 0,
            Keys.D2 or Keys.NumPad2 => 1,
            Keys.D3 or Keys.NumPad3 => 2,
            Keys.D4 or Keys.NumPad4 => 3,
            Keys.D5 or Keys.NumPad5 => 4,
            Keys.D6 or Keys.NumPad6 => 5,
            _ => -1
        };

        if (requested >= 0)
        {
            ApplyPreset(TargetResolutionCatalog.All[requested]);
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.F11)
        {
            currentPresetIndex =
                (currentPresetIndex + 1) % TargetResolutionCatalog.All.Length;
            ApplyPreset(TargetResolutionCatalog.All[currentPresetIndex]);
            e.Handled = true;
            return;
        }

        if (!e.Control)
        {
            return;
        }

        Point delta = e.KeyCode switch
        {
            Keys.Left => new Point(-100, 0),
            Keys.Right => new Point(100, 0),
            Keys.Up => new Point(0, -100),
            Keys.Down => new Point(0, 100),
            _ => Point.Empty
        };

        if (delta == Point.Empty)
        {
            return;
        }

        Location = new Point(Location.X + delta.X, Location.Y + delta.Y);
        e.Handled = true;
    }

    private void ApplyPreset(TargetResolutionPreset preset)
    {
        currentPresetIndex = Array.IndexOf(TargetResolutionCatalog.All, preset);
        Size = preset.Size;
        UpdateGeometry();
    }

    private void UpdateGeometry()
    {
        Screen screen = Screen.FromHandle(Handle);
        geometryLabel.Text =
            $"OUTER {Width}x{Height} px   CLIENT {ClientSize.Width}x{ClientSize.Height} px   DPI {DeviceDpi}\r\n"
            + $"POSITION {Left},{Top}   MONITOR {screen.Bounds.Width}x{screen.Bounds.Height}   "
            + $"WORK {screen.WorkingArea.Width}x{screen.WorkingArea.Height} @ "
            + $"{screen.WorkingArea.Left},{screen.WorkingArea.Top}";
    }

    private static int FindNearestPresetIndex(Size size)
    {
        int bestIndex = 0;
        long bestDistance = long.MaxValue;

        for (int index = 0; index < TargetResolutionCatalog.All.Length; index++)
        {
            TargetResolutionPreset preset = TargetResolutionCatalog.All[index];
            long dx = preset.Width - size.Width;
            long dy = preset.Height - size.Height;
            long distance = dx * dx + dy * dy;

            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            bestIndex = index;
        }

        return bestIndex;
    }
}
