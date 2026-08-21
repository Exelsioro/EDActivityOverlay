using System.IO;
using System.Windows;
using ED_Inara_Overlay.Services;

namespace ED_Inara_Overlay.Windows;

public partial class X52ControlHelpWindow : Window
{
    public X52ControlHelpWindow()
    {
        InitializeComponent();
        string? profile = FindCurrentProfile();
        ProfilePathText.Text = profile is null
            ? Loc.Get("Loc_X52_PROFILE_NOT_FOUND")
            : Loc.Format("Loc_X52_CURRENT_PROFILE_FORMAT", profile);

        string document = Path.Combine(AppContext.BaseDirectory, "Documentation", "X52_CONTROL_CHEATSHEET_RU.md");
        CheatsheetText.Text = File.Exists(document)
            ? File.ReadAllText(document)
            : Loc.Get("Loc_X52_CHEATSHEET_NOT_FOUND");
    }

    private static string? FindCurrentProfile()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
            "Logitech", "X52 Professional");
        if (!Directory.Exists(directory)) return null;
        return Directory.EnumerateFiles(directory, "*_Overlay.pr0", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
