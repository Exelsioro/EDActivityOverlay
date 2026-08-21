using ED_Inara_Overlay.Models;

namespace ED_Inara_Overlay.Services.Engineering;

public sealed record EngineerDefinition(
    string Name,
    string SystemName,
    string BaseName,
    string BodyName,
    bool IsOnFoot,
    string DiscoveryKey,
    string MeetingKey,
    string UnlockKey)
{
    public string Discovery => Loc.Get(DiscoveryKey);
    public string Meeting => Loc.Get(MeetingKey);
    public string Unlock => Loc.Get(UnlockKey);
    public string WikiUrl
    {
        get
        {
            string article = Name switch
            {
                "Tod 'The Blaster' McQuinn" => "Tod_%22The_Blaster%22_McQuinn",
                "Professor Palin" => "Ishmael_Palin",
                "Colonel Bris Dekker" => "Bris_Dekker",
                _ => Uri.EscapeDataString(Name.Replace(' ', '_'))
            };
            return $"https://elite-dangerous.fandom.com/wiki/{article}";
        }
    }
}

/// <summary>
/// Offline ship and Odyssey Engineer directory. Location and unlock data is adapted from
/// EDDiscovery/EliteDangerousCore (Apache-2.0); journal progress is overlaid at runtime.
/// </summary>
public static class EngineerCatalog
{
    public static IReadOnlyList<EngineerDefinition> All { get; } =
    [
        E("Baltanos", "Deriso", "The Divine Apparatus", "3 A", true),
        E("Bill Turner", "Alioth", "Turner Metallics Inc", "4 A"),
        E("Broo Tarquin", "Muang", "Broo's Legacy", "5 A"),
        E("Chloe Sedesi", "Shenve", "Cinder Dock", "A 6"),
        E("Colonel Bris Dekker", "Sol", "Dekker's Yard", "Iapetus"),
        E("Didi Vatermann", "Leesti", "Vatermann LLC", "1 A"),
        E("Domino Green", "Orishis", "The Jackrabbit", "4", true),
        E("Eleanor Bresa", "Desy", "Bresa Modifications", "7 A", true),
        E("Elvira Martuuk", "Khun", "Long Sight Base", "5"),
        E("Etienne Dorn", "Los", "Kraken's Retreat", "A 2 B"),
        E("Felicity Farseer", "Deciat", "Farseer Inc", "6 A"),
        E("Hera Tani", "Kuwemaki", "The Jet's Hole", "A 3 A"),
        E("Hero Ferrari", "Siris", "Nevermore Terrace", "5 C", true),
        E("Jude Navarro", "Aurai", "Marshall's Drift", "1 A", true),
        E("Juri Ishmaak", "Giryak", "Pater's Memorial", "2 A"),
        E("Kit Fowler", "Capoya", "The Last Call", "2", true),
        E("Lei Cheung", "Laksak", "Trader's Rest", "A 1"),
        E("Liz Ryder", "Eurybia", "Demolition Unlimited", "Makalu"),
        E("Lori Jameson", "Shinrarta Dezhra", "Jameson Base", "A 1"),
        E("Marco Qwent", "Sirius", "Qwent Research Base", "Lucifer"),
        E("Marsha Hicks", "Tir", "The Watchtower", "A 2"),
        E("Mel Brandon", "Luchtaine", "The Brig", "A 1 C"),
        E("Oden Geiger", "Candiaei", "Ankh's Promise", "9 C", true),
        E("Petra Olmanova", "Asura", "Sanctuary", "1 A"),
        E("Professor Palin", "Arque", "Abel Laboratory", "4 E"),
        E("Ram Tah", "Meene", "Phoenix Base", "AB 5 D"),
        E("Rosa Dayette", "Kojeara", "Rosa's Shop", "4 B", true),
        E("Selene Jean", "Kuk", "Prospector's Rest", "B 3"),
        E("Terra Velasquez", "Shou Xing", "Rascal's Choice", "1", true),
        E("The Dweller", "Wyrd", "Black Hide", "A 2"),
        E("The Sarge", "Beta-3 Tucani", "The Beach", "2 B A"),
        E("Tiana Fortune", "Achenar", "Fortune's Loss", "4 A"),
        E("Tod 'The Blaster' McQuinn", "Wolf 397", "Trophy Camp", "Trus Madi"),
        E("Uma Laszlo", "Xuane", "Laszlo's Resolve", "A 3", true),
        E("Wellington Beck", "Jolapa", "Beck Facility", "6 A", true),
        E("Yarden Bond", "Bayan", "Salamander Bank", "7 B", true),
        E("Yi Shen", "Einheriar", "Eidolon Hold", "1 A", true),
        E("Zacariah Nemo", "Yoru", "Nemo Cyber Party Base", "4")
    ];

    private static EngineerDefinition E(string name, string system, string baseName, string body, bool onFoot = false)
    {
        string key = new(name.Where(char.IsLetterOrDigit).ToArray());
        return new EngineerDefinition(name, system, baseName, body, onFoot,
            $"Loc_Engineer_{key}_Discovery",
            $"Loc_Engineer_{key}_Meeting",
            $"Loc_Engineer_{key}_Unlock");
    }

    public static EngineerProgressEntry? FindProgress(
        EngineerDefinition engineer,
        IReadOnlyDictionary<string, EngineerProgressEntry> progress)
    {
        string key = MaterialName.Normalize(engineer.Name);
        if (progress.TryGetValue(key, out EngineerProgressEntry? exact))
        {
            return exact;
        }

        // The journal has used both the full nickname and shortened Tod McQuinn form.
        return progress.Values.FirstOrDefault(item =>
            MaterialName.Normalize(item.Name).Contains("todmcquinn", StringComparison.OrdinalIgnoreCase)
            && key.Contains("todtheblastermcquinn", StringComparison.OrdinalIgnoreCase));
    }
}
