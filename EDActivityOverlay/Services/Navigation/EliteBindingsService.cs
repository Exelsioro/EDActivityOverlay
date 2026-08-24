using System.IO;
using System.Xml.Linq;

namespace EDActivityOverlay.Services.Navigation;

public enum EliteKeyboardLayout
{
    Us,
    Russian
}

public sealed record EliteResolvedKey(
    ushort VirtualKey,
    ushort ScanCode,
    bool Extended,
    string FrontierToken);

public sealed record EliteKeyBinding(
    ushort VirtualKey,
    IReadOnlyList<ushort> Modifiers,
    string DisplayName)
{
    public ushort ScanCode { get; init; }
    public bool Extended { get; init; }
    public IReadOnlyList<ushort> ModifierScanCodes { get; init; } =
        Array.Empty<ushort>();
    public IReadOnlyList<bool> ModifierExtended { get; init; } =
        Array.Empty<bool>();
    public string FrontierToken { get; init; } = string.Empty;
    public EliteKeyboardLayout DetectedLayout { get; init; } =
        EliteKeyboardLayout.Us;
}

public sealed record EliteNavigationBindings(
    string PresetName,
    string FilePath,
    EliteKeyBinding GalaxyMap,
    EliteKeyBinding NextPanel,
    EliteKeyBinding Select);

public sealed record EliteBindingsFileOption(
    string FilePath,
    string FileName,
    string PresetName,
    DateTime LastWriteUtc)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(PresetName)
            ? FileName
            : $"{FileName}  [{PresetName}]";
}

public static class EliteBindingsService
{
    private const uint KlfNotTellShell = 0x00000080;
    private const uint MapVkVkToVscEx = 4;

    [System.Runtime.InteropServices.DllImport(
        "user32.dll",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr LoadKeyboardLayout(
        string pwszKLID,
        uint flags);

    [System.Runtime.InteropServices.DllImport(
        "user32.dll",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern short VkKeyScanEx(
        char character,
        IntPtr keyboardLayout);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(
        uint code,
        uint mapType,
        IntPtr keyboardLayout);

    public static string DefaultBindingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Frontier Developments", "Elite Dangerous", "Options", "Bindings");

    public static EliteNavigationBindings Detect(
        string? bindingsDirectory = null,
        string? presetOverride = null,
        string? fileOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(fileOverride))
        {
            string selectedFile =
                Path.GetFullPath(fileOverride.Trim());

            if (!File.Exists(selectedFile))
            {
                throw new FileNotFoundException(
                    "Selected Elite bindings file was not found.",
                    selectedFile);
            }

            if (!selectedFile.EndsWith(
                    ".binds",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Selected Elite controls file must have the .binds extension.");
            }

            XElement selectedRoot =
                XDocument.Load(selectedFile).Root
                ?? throw new InvalidDataException(
                    "Elite bindings XML has no root element.");

            string selectedPreset =
                ((string?)selectedRoot.Attribute("PresetName")
                 ?? Path.GetFileNameWithoutExtension(selectedFile))
                .Trim();

            return BuildBindings(
                selectedPreset,
                selectedFile,
                selectedRoot);
        }

        string directory = string.IsNullOrWhiteSpace(bindingsDirectory)
            ? DefaultBindingsDirectory
            : bindingsDirectory;
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(directory);

        string preset = string.IsNullOrWhiteSpace(presetOverride)
            ? ReadShipPreset(directory)
            : presetOverride.Trim();
        string file = Directory.EnumerateFiles(directory, "*.binds")
            .Select(path => new { Path = path, Root = TryLoad(path) })
            .Where(item => item.Root is not null
                           && string.Equals((string?)item.Root.Attribute("PresetName"), preset,
                               StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => File.GetLastWriteTimeUtc(item.Path))
            .Select(item => item.Path)
            .FirstOrDefault()
            ?? throw new InvalidDataException($"Active Elite preset '{preset}' was not found.");

        XElement root = XDocument.Load(file).Root
            ?? throw new InvalidDataException("Elite bindings XML has no root element.");

        return BuildBindings(
            preset,
            file,
            root);
    }

    private static EliteNavigationBindings BuildBindings(
        string preset,
        string file,
        XElement root) =>
        new(
            preset,
            file,
            ReadKeyboardBinding(root, "GalaxyMapOpen"),
            ReadKeyboardBinding(root, "CycleNextPanel"),
            ReadKeyboardBinding(root, "UI_Select"));

    public static IReadOnlyList<EliteBindingsFileOption> ListBindingFiles(
        string? bindingsDirectory = null)
    {
        string directory = string.IsNullOrWhiteSpace(bindingsDirectory)
            ? DefaultBindingsDirectory
            : bindingsDirectory;

        if (!Directory.Exists(directory))
        {
            return Array.Empty<EliteBindingsFileOption>();
        }

        return Directory
            .EnumerateFiles(directory, "*.binds")
            .Select(path =>
            {
                XElement? root = TryLoad(path);
                string preset =
                    ((string?)root?.Attribute("PresetName")
                     ?? string.Empty).Trim();

                return new EliteBindingsFileOption(
                    path,
                    Path.GetFileName(path),
                    preset,
                    File.GetLastWriteTimeUtc(path));
            })
            .OrderByDescending(item => item.LastWriteUtc)
            .ThenBy(
                item => item.FileName,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> ListPresets(string? bindingsDirectory = null)
    {
        string directory = string.IsNullOrWhiteSpace(bindingsDirectory)
            ? DefaultBindingsDirectory
            : bindingsDirectory;
        if (!Directory.Exists(directory)) return Array.Empty<string>();
        return Directory.EnumerateFiles(directory, "*.binds")
            .Select(TryLoad)
            .Where(root => root is not null)
            .Select(root => ((string?)root!.Attribute("PresetName") ?? string.Empty).Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static string ReadShipPreset(string directory)
    {
        string startFile = Path.Combine(directory, "StartPreset.4.start");
        if (!File.Exists(startFile))
            throw new FileNotFoundException("Elite active preset file was not found.", startFile);
        string preset = File.ReadLines(startFile)
            .Select(line => line.Trim().TrimStart('\uFEFF'))
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(preset))
            throw new InvalidDataException("Elite active preset file is empty.");
        return preset;
    }

    private static XElement? TryLoad(string path)
    {
        try { return XDocument.Load(path).Root; }
        catch { return null; }
    }

    private static EliteKeyBinding ReadKeyboardBinding(
        XElement root,
        string action)
    {
        XElement node = root.Element(action)
            ?? throw new InvalidDataException(
                $"Elite action '{action}' is missing from the active preset.");

        XElement binding = node.Elements()
            .FirstOrDefault(element =>
                string.Equals(
                    (string?)element.Attribute("Device"),
                    "Keyboard",
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                $"Elite action '{action}' has no keyboard binding.");

        EliteKeyboardLayout layout =
            DetectKeyboardLayout(root);

        string keyName =
            (string?)binding.Attribute("Key")
            ?? string.Empty;

        EliteResolvedKey key =
            ResolvePhysicalKey(
                keyName,
                layout);

        EliteResolvedKey[] modifiers =
            binding.Elements("Modifier")
                .Where(element =>
                    string.Equals(
                        (string?)element.Attribute("Device"),
                        "Keyboard",
                        StringComparison.OrdinalIgnoreCase))
                .Select(element =>
                    ResolvePhysicalKey(
                        (string?)element.Attribute("Key")
                        ?? string.Empty,
                        layout))
                .ToArray();

        string display =
            modifiers.Length == 0
                ? keyName.Replace(
                    "Key_",
                    string.Empty)
                : string.Join(
                    "+",
                    modifiers
                        .Select(item =>
                            VirtualKeyName(item.VirtualKey))
                        .Append(
                            keyName.Replace(
                                "Key_",
                                string.Empty)));

        return new EliteKeyBinding(
            key.VirtualKey,
            modifiers
                .Select(item => item.VirtualKey)
                .ToArray(),
            display)
        {
            ScanCode = key.ScanCode,
            Extended = key.Extended,
            ModifierScanCodes = modifiers
                .Select(item => item.ScanCode)
                .ToArray(),
            ModifierExtended = modifiers
                .Select(item => item.Extended)
                .ToArray(),
            FrontierToken = keyName,
            DetectedLayout = layout
        };
    }

    public static EliteKeyboardLayout DetectKeyboardLayout(
        XElement root)
    {
        bool containsCyrillic =
            root.DescendantsAndSelf()
                .Attributes("Key")
                .Select(attribute =>
                    attribute.Value)
                .Any(ContainsCyrillic);

        return containsCyrillic
            ? EliteKeyboardLayout.Russian
            : EliteKeyboardLayout.Us;
    }

    public static EliteResolvedKey ResolvePhysicalKey(
        string frontierKey,
        EliteKeyboardLayout layout)
    {
        string name =
            frontierKey.StartsWith(
                "Key_",
                StringComparison.OrdinalIgnoreCase)
                ? frontierKey[4..]
                : frontierKey;

        IntPtr keyboardLayout =
            LoadKeyboardLayout(
                layout == EliteKeyboardLayout.Russian
                    ? "00000419"
                    : "00000409",
                KlfNotTellShell);

        if (keyboardLayout == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Windows keyboard layout '{layout}' could not be loaded.");
        }

        if (name.Length == 1
            && IsCyrillic(name[0]))
        {
            return ResolveCharacterKey(
                name[0],
                frontierKey,
                keyboardLayout);
        }

        if (TryGetNamedCharacter(
                name,
                out char namedCharacter))
        {
            return ResolveCharacterKey(
                namedCharacter,
                frontierKey,
                keyboardLayout);
        }

        ushort virtualKey =
            ToVirtualKey(frontierKey);

        return ResolveVirtualKey(
            virtualKey,
            frontierKey,
            keyboardLayout);
    }

    private static EliteResolvedKey ResolveCharacterKey(
        char character,
        string frontierToken,
        IntPtr keyboardLayout)
    {
        short result =
            VkKeyScanEx(
                character,
                keyboardLayout);

        if (result == -1)
        {
            throw new InvalidDataException(
                $"Keyboard character '{character}' from '{frontierToken}' cannot be resolved in the detected Windows layout.");
        }

        ushort virtualKey =
            (ushort)(result & 0x00FF);

        return ResolveVirtualKey(
            virtualKey,
            frontierToken,
            keyboardLayout);
    }

    private static EliteResolvedKey ResolveVirtualKey(
        ushort virtualKey,
        string frontierToken,
        IntPtr keyboardLayout)
    {
        uint mapped =
            MapVirtualKeyEx(
                virtualKey,
                MapVkVkToVscEx,
                keyboardLayout);

        if (mapped == 0)
        {
            throw new InvalidDataException(
                $"Windows scan code for '{frontierToken}' could not be resolved.");
        }

        ushort scanCode =
            (ushort)(mapped & 0x00FF);

        bool extended =
            (mapped & 0xFF00) != 0;

        return new EliteResolvedKey(
            virtualKey,
            scanCode,
            extended,
            frontierToken);
    }

    private static bool TryGetNamedCharacter(
        string name,
        out char character)
    {
        character =
            name.ToLowerInvariant() switch
            {
                "slash" => '/',
                "period" => '.',
                "comma" => ',',
                "minus" => '-',
                "equals" => '=',
                "leftbracket" => '[',
                "rightbracket" => ']',
                "semicolon" => ';',
                "apostrophe" => '\'',
                "grave" => '`',
                "backslash" => '\\',
                _ => '\0'
            };

        return character != '\0';
    }

    private static bool ContainsCyrillic(
        string value) =>
        value.Any(IsCyrillic);

    private static bool IsCyrillic(
        char value) =>
        value is >= '\u0400' and <= '\u04FF';

    public static ushort ToVirtualKey(string frontierKey)
    {
        string name = frontierKey.StartsWith("Key_", StringComparison.OrdinalIgnoreCase)
            ? frontierKey[4..]
            : frontierKey;
        if (name.Length == 1 && name[0] <= 0x7F && char.IsLetterOrDigit(name[0]))
            return char.ToUpperInvariant(name[0]);
        if (name.Length == 1 && TryMapRussianPhysicalKey(name[0], out ushort russianPhysicalKey))
            return russianPhysicalKey;
        if (name.StartsWith('F') && int.TryParse(name[1..], out int function) && function is >= 1 and <= 24)
            return checked((ushort)(0x70 + function - 1));
        return name.ToLowerInvariant() switch
        {
            "space" => 0x20,
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "backspace" => 0x08,
            "escape" => 0x1B,
            "leftarrow" => 0x25,
            "uparrow" => 0x26,
            "rightarrow" => 0x27,
            "downarrow" => 0x28,
            "leftcontrol" or "rightcontrol" or "control" => 0x11,
            "leftshift" or "rightshift" or "shift" => 0x10,
            "leftalt" or "rightalt" or "alt" => 0x12,
            "slash" => 0xBF,
            "period" => 0xBE,
            "comma" => 0xBC,
            "minus" => 0xBD,
            "equals" => 0xBB,
            "leftbracket" => 0xDB,
            "rightbracket" => 0xDD,
            "semicolon" => 0xBA,
            "apostrophe" => 0xDE,
            "grave" => 0xC0,
            "backslash" => 0xDC,
            _ => throw new InvalidDataException($"Unsupported Elite keyboard key '{frontierKey}'.")
        };
    }

    private static bool TryMapRussianPhysicalKey(char value, out ushort key)
    {
        const string russian = "йцукенгшщзхъфывапролджэячсмитьбю";
        const string physical = "qwertyuiop[]asdfghjkl;'zxcvbnm,.";
        int index = russian.IndexOf(char.ToLowerInvariant(value));
        if (index < 0)
        {
            key = 0;
            return false;
        }
        char physicalKey = physical[index];
        key = physicalKey switch
        {
            '[' => 0xDB,
            ']' => 0xDD,
            ';' => 0xBA,
            '\'' => 0xDE,
            ',' => 0xBC,
            '.' => 0xBE,
            _ => ToVirtualKey("Key_" + physicalKey)
        };
        return true;
    }

    private static string VirtualKeyName(ushort key) => key switch
    {
        0x10 => "Shift",
        0x11 => "Ctrl",
        0x12 => "Alt",
        _ => $"VK_{key:X2}"
    };
}
