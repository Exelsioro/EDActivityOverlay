using System.IO;
using System.Xml.Linq;

namespace ED_Inara_Overlay.Services.Navigation;

public sealed record EliteKeyBinding(ushort VirtualKey, IReadOnlyList<ushort> Modifiers, string DisplayName);

public sealed record EliteNavigationBindings(
    string PresetName,
    string FilePath,
    EliteKeyBinding GalaxyMap,
    EliteKeyBinding NextPanel,
    EliteKeyBinding Select);

public static class EliteBindingsService
{
    public static string DefaultBindingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Frontier Developments", "Elite Dangerous", "Options", "Bindings");

    public static EliteNavigationBindings Detect(string? bindingsDirectory = null, string? presetOverride = null)
    {
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
        return new EliteNavigationBindings(
            preset,
            file,
            ReadKeyboardBinding(root, "GalaxyMapOpen"),
            ReadKeyboardBinding(root, "CycleNextPanel"),
            ReadKeyboardBinding(root, "UI_Select"));
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

    private static EliteKeyBinding ReadKeyboardBinding(XElement root, string action)
    {
        XElement node = root.Element(action)
            ?? throw new InvalidDataException($"Elite action '{action}' is missing from the active preset.");
        XElement binding = node.Elements()
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("Device"), "Keyboard",
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Elite action '{action}' has no keyboard binding.");
        string keyName = (string?)binding.Attribute("Key") ?? string.Empty;
        ushort key = ToVirtualKey(keyName);
        ushort[] modifiers = binding.Elements("Modifier")
            .Where(element => string.Equals((string?)element.Attribute("Device"), "Keyboard",
                StringComparison.OrdinalIgnoreCase))
            .Select(element => ToVirtualKey((string?)element.Attribute("Key") ?? string.Empty))
            .ToArray();
        string display = modifiers.Length == 0
            ? keyName.Replace("Key_", string.Empty)
            : string.Join("+", modifiers.Select(VirtualKeyName).Append(keyName.Replace("Key_", string.Empty)));
        return new EliteKeyBinding(key, modifiers, display);
    }

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
