using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Engineering;
using EDActivityOverlay.Services;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Windows;

public partial class EngineeringWindow : Window
{
    private readonly EngineeringService service = EngineeringService.Instance;
    private readonly MaterialAcquisitionAdvisor materialAdvisor = new();
    private readonly MainWindow? parentWindow;
    private readonly bool overlayMode;
    private IntPtr targetWindow;
    private EngineeringSnapshot snapshot = EngineeringSnapshot.Empty;
    private string placement = "MiddleRight";
    private bool fullAssistantVisible;
    private string? widgetHelpMaterialId;
    private EngineerRow? selectedEngineer;
    private EngineerBlueprintRow? selectedEngineerBlueprint;

    public bool IsOverlayMode => overlayMode;

    public EngineeringWindow() : this(null)
    {
    }

    public EngineeringWindow(MainWindow? parentWindow)
    {
        this.parentWindow = parentWindow;
        overlayMode = parentWindow is not null;
        targetWindow = parentWindow?.TargetWindowHandle ?? IntPtr.Zero;
        InitializeComponent();
        CategoryFilter.ItemsSource = EngineeringLocalization.CategoryFilters;
        CategoryFilter.SelectedIndex = 0;
        if (overlayMode)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            OverlayCloseButton.Visibility = Visibility.Visible;
            FullAssistantPanel.Visibility = Visibility.Collapsed;
            EngineeringWidgetPanel.Visibility = Visibility.Visible;
            MinWidth = 0;
            MinHeight = 0;
            Width = 420;
            Height = 340;
            SetChromeStyle(SettingsService.Instance.Settings.OverlayChromeStyle);
        }
        service.StateChanged += OnStateChanged;
        service.Catalog.CatalogChanged += OnCatalogChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (overlayMode)
        {
            WindowsAPI.SetupOverlayWindow(this);
            PositionOverTarget();
            WindowsAPI.SetTopmost(this, true);
        }
        ApplyCatalog();
        ApplySnapshot(service.Current);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (fullAssistantVisible)
        {
            parentWindow?.EndExclusiveOverlayInteraction();
        }
        service.StateChanged -= OnStateChanged;
        service.Catalog.CatalogChanged -= OnCatalogChanged;
        parentWindow?.OnEngineeringOverlayClosed();
    }

    private void OnStateChanged(object? sender, EngineeringStateChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => ApplySnapshot(e.State)));

    private void OnCatalogChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ApplyCatalog();
            ApplySnapshot(service.Current);
        }));

    private void ApplySnapshot(EngineeringSnapshot value)
    {
        snapshot = value;
        CommanderText.Text = string.IsNullOrWhiteSpace(value.Commander)
            ? Loc.Get("Loc_Commander_Unknown")
            : Loc.Format("Loc_Commander_Format", value.Commander.ToUpperInvariant());
        SummaryText.Text = Loc.Format("Loc_Engineering_Summary_Format", value.MaterialKinds, value.MissingKinds, value.Wishlist.Count);
        WidgetSummaryText.Text = SummaryText.Text;
        MaterialRequirement[] missing = value.Requirements
            .Where(requirement => !requirement.IsComplete)
            .OrderByDescending(requirement => requirement.Missing)
            .ToArray();
        WidgetRequirementsList.ItemsSource = missing.Length == 0
            ? [WidgetRequirementRow.Completed(Loc.Get("Loc_Widget_all_materials_ready"))]
            : missing.Select(requirement =>
            {
                MaterialAcquisitionAdvice? advice = value.Advice.FirstOrDefault(item =>
                    item.MaterialId.Equals(requirement.MaterialId, StringComparison.OrdinalIgnoreCase));
                AcquisitionOption? destination = advice?.Options
                    .OrderBy(option => option.Priority)
                    .FirstOrDefault(option => !string.IsNullOrWhiteSpace(option.SystemName));
                return new WidgetRequirementRow(
                    requirement.Name,
                    requirement.ProgressText,
                    destination?.SystemName,
                    advice);
            }).ToArray();
        AcquisitionOption? widgetDestination = value.Advice
            .SelectMany(advice => advice.Options)
            .Where(option => !string.IsNullOrWhiteSpace(option.SystemName))
            .OrderBy(option => option.Priority)
            .FirstOrDefault();
        WidgetDestinationText.Text = widgetDestination is null
            ? Loc.Get("Loc_Widget_no_destination")
            : Loc.Format("Loc_Widget_destination_format", widgetDestination.Destination);
        if (!string.IsNullOrWhiteSpace(widgetHelpMaterialId)
            && value.Advice.FirstOrDefault(advice => advice.MaterialId.Equals(
                widgetHelpMaterialId, StringComparison.OrdinalIgnoreCase)) is MaterialAcquisitionAdvice activeHelp)
        {
            PopulateWidgetHelp(activeHelp);
        }
        WishlistGrid.ItemsSource = value.Wishlist;
        RequirementsGrid.ItemsSource = value.Requirements;
        TrackedMaterialsGrid.ItemsSource = value.TrackedMaterials
            .Select(tracked =>
            {
                MaterialInventoryEntry material = value.Inventory.TryGetValue(tracked.MaterialId, out MaterialInventoryEntry? found)
                    ? found
                    : new MaterialInventoryEntry(tracked.MaterialId,
                        EngineeringLocalization.MaterialName(tracked.MaterialId, tracked.DisplayName), tracked.Category, 0);
                MaterialAcquisitionAdvice advice = value.Advice.FirstOrDefault(item =>
                    item.MaterialId.Equals(tracked.MaterialId, StringComparison.OrdinalIgnoreCase))
                    ?? materialAdvisor.Create(new MaterialRequirement(
                        material.Id, material.Name, material.Category, tracked.TargetCount, material.Count));
                return new TrackedRow(material, tracked.TargetCount, advice);
            })
            .OrderBy(row => row.Name)
            .ToArray();
        string? selectedEngineerName = (EngineersGrid.SelectedItem as EngineerRow)?.Name;
        EngineerRow[] engineerRows = EngineerCatalog.All
            .Select(definition => EngineerRow.Create(definition, EngineerCatalog.FindProgress(definition, value.Engineers)))
            .OrderBy(engineer => engineer.Name)
            .ToArray();
        EngineersGrid.ItemsSource = engineerRows;
        EngineersGrid.SelectedItem = engineerRows.FirstOrDefault(engineer => engineer.Name == selectedEngineerName)
            ?? engineerRows.FirstOrDefault();
        AdviceGrid.ItemsSource = value.Advice.Select(advice =>
        {
            AcquisitionOption best = advice.Options.OrderBy(option => option.Priority).First();
            AcquisitionOption? destination = advice.Options
                .OrderBy(option => option.Priority)
                .FirstOrDefault(option => !string.IsNullOrWhiteSpace(option.SystemName));
            return new AdviceRow(
                advice.MaterialName,
                advice.Missing,
                best.Title,
                destination?.Destination ?? Loc.Get("Loc_Dynamic_nearby_search"),
                best.Instructions,
                destination?.SystemName,
                advice);
        }).ToArray();
        ApplyInventoryFilter();
    }

    private void ApplyCatalog()
    {
        string search = RecipeSearchBox.Text?.Trim() ?? string.Empty;
        BlueprintRecipe[] recipes = service.Catalog.Recipes
            .Where(recipe => string.IsNullOrWhiteSpace(search)
                || recipe.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || recipe.Ingredients.Any(ingredient => ingredient.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        object? selected = RecipeCombo.SelectedItem;
        RecipeCombo.ItemsSource = recipes;
        if (selected is BlueprintRecipe old)
        {
            RecipeCombo.SelectedItem = recipes.FirstOrDefault(recipe => recipe.Id == old.Id);
        }
        if (RecipeCombo.SelectedIndex < 0 && recipes.Length > 0)
        {
            RecipeCombo.SelectedIndex = 0;
        }
        CatalogStatusText.Text = Loc.Format("Loc_Catalog_Count_Format", service.Catalog.Recipes.Count);
    }

    public void RefreshLocalization()
    {
        EngineeringMaterialCategory? selectedCategory =
            (CategoryFilter.SelectedItem as EngineeringLocalization.CategoryFilter)?.Category;
        CategoryFilter.ItemsSource = EngineeringLocalization.CategoryFilters;
        CategoryFilter.SelectedItem = EngineeringLocalization.CategoryFilters
            .FirstOrDefault(option => option.Category == selectedCategory);
        CategoryFilter.SelectedIndex = Math.Max(CategoryFilter.SelectedIndex, 0);
        ApplyCatalog();
        ApplySnapshot(service.Current);
    }

    private void ApplyInventoryFilter()
    {
        string search = InventorySearchBox.Text?.Trim() ?? string.Empty;
        EngineeringLocalization.CategoryFilter category = CategoryFilter.SelectedItem as EngineeringLocalization.CategoryFilter
            ?? EngineeringLocalization.CategoryFilters[0];
        Dictionary<string, MaterialInventoryEntry> allMaterials = new(snapshot.Inventory, StringComparer.OrdinalIgnoreCase);
        foreach (BlueprintIngredient ingredient in service.Catalog.Recipes.SelectMany(recipe => recipe.Ingredients))
        {
            if (!allMaterials.ContainsKey(ingredient.MaterialId))
            {
                allMaterials[ingredient.MaterialId] = new MaterialInventoryEntry(
                    ingredient.MaterialId,
                    EngineeringLocalization.MaterialName(ingredient.MaterialId, ingredient.Name),
                    materialAdvisor.InferCategory(ingredient.MaterialId, snapshot.Inventory),
                    0);
            }
        }

        InventoryGrid.ItemsSource = allMaterials.Values
            .Where(item => string.IsNullOrWhiteSpace(search)
                || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || item.Id.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(item => category.Category is null || item.Category == category.Category)
            .OrderBy(item => item.Category)
            .ThenBy(item => item.Name)
            .Select(item => new InventoryRow(
                item,
                snapshot.TrackedMaterials.Any(tracked => tracked.MaterialId.Equals(
                    item.Id, StringComparison.OrdinalIgnoreCase))))
            .ToArray();
    }

    private void InventoryFilterChanged(object sender, EventArgs e)
    {
        if (InventoryGrid is not null)
        {
            ApplyInventoryFilter();
        }
    }

    private void RecipeSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (RecipeCombo is not null)
        {
            ApplyCatalog();
        }
    }

    private void AddRecipe_Click(object sender, RoutedEventArgs e)
    {
        if (RecipeCombo.SelectedItem is not BlueprintRecipe recipe)
        {
            return;
        }
        int count = int.TryParse(CraftCountBox.Text, out int parsed) ? Math.Clamp(parsed, 1, 99) : 1;
        CraftCountBox.Text = count.ToString();
        service.AddOrIncreaseWishlist(recipe, count);
    }

    public void SetTargetWindow(IntPtr window)
    {
        targetWindow = window;
        if (IsLoaded) PositionOverTarget();
    }

    public void SetPlacement(string value)
    {
        placement = string.IsNullOrWhiteSpace(value) ? "MiddleRight" : value;
        PositionOverTarget();
    }

    public void SetChromeStyle(string value)
    {
        string normalized = OverlayChromeStyles.Normalize(value);
        OverlayChromeHelper.Apply(EngineeringWidgetPanel, normalized);
        OverlayChromeHelper.Apply(EngineeringWidgetHelpPanel, normalized);
    }

    public void ApplyInteractionMode(bool canInteract, bool showCursor)
    {
        if (!overlayMode || !IsLoaded) return;
        WindowsAPI.SetClickThrough(this, !canInteract);
        WidgetInteractionHint.Text = canInteract ? Loc.Get("Loc_DRAG_TO_MOVE") : Loc.Get("Loc_CTRL_6_INTERACT");
        if (canInteract && showCursor) WindowsAPI.EnsureCursorVisibleOnWindow(this);
        else WindowsAPI.RestoreCursorVisibility();
        WindowsAPI.SetTopmost(this, true);
    }

    private void PositionOverTarget()
    {
        if (!overlayMode || targetWindow == IntPtr.Zero
            || !WindowsAPI.TryGetWindowRectDips(targetWindow, out WindowsAPI.RECT rect)) return;
        double targetWidth = rect.Right - rect.Left;
        double targetHeight = rect.Bottom - rect.Top;
        if (fullAssistantVisible)
        {
            Width = Math.Min(1180, Math.Max(MinWidth, targetWidth - 64));
            Height = Math.Min(760, Math.Max(MinHeight, targetHeight - 64));
            Left = rect.Left + (targetWidth - Width) / 2.0;
            Top = rect.Top + (targetHeight - Height) / 2.0;
            return;
        }

        Rect workArea = WindowsAPI.GetMonitorWorkArea(targetWindow);
        double left;
        double top;
        (left, top) = OverlayLayoutHelper.GetPinnedPosition(rect, Width, Height, placement, 16);
        OverlayLayoutHelper.ClampPosition(ref left, ref top, Width, Height, workArea, 10, 10);
        Left = left;
        Top = top;
    }

    private void OverlayCloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenFullAssistantButton_Click(object sender, RoutedEventArgs e)
    {
        if (!overlayMode || fullAssistantVisible) return;
        fullAssistantVisible = true;
        EngineeringWidgetPanel.Visibility = Visibility.Collapsed;
        EngineeringWidgetHelpPanel.Visibility = Visibility.Collapsed;
        FullAssistantPanel.Visibility = Visibility.Visible;
        MinWidth = 920;
        MinHeight = 620;
        PositionOverTarget();
        parentWindow?.BeginExclusiveOverlayInteraction();
        Activate();
        WindowsAPI.TryActivateWindow(new WindowInteropHelper(this).Handle);
        WindowsAPI.EnsureCursorVisibleOnWindow(this);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!overlayMode || e.LeftButton != MouseButtonState.Pressed) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void RemoveWishlist_Click(object sender, RoutedEventArgs e)
    {
        if (WishlistGrid.SelectedItem is WishlistEntry item)
        {
            service.RemoveWishlist(item.Id);
        }
    }

    private async void RefreshCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (!RefreshCatalogButton.IsEnabled)
        {
            return;
        }

        RefreshCatalogButton.IsEnabled = false;
        CatalogStatusText.Text = Loc.Get("Loc_Catalog_Updating");
        try
        {
            await Task.Run(() => service.Catalog.LoadAsync());
            ApplyCatalog();
        }
        catch (Exception ex)
        {
            CatalogStatusText.Text = Loc.Get("Loc_Catalog_Update_Failed");
            Logger.Logger.Warning($"Engineering catalog refresh failed: {ex.Message}");
        }
        finally
        {
            RefreshCatalogButton.IsEnabled = true;
        }
    }

    private void ToggleMaterialTracking_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: InventoryRow row })
        {
            service.ToggleTrackedMaterial(row.Material);
        }
    }

    private void UntrackMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: TrackedRow row })
        {
            service.ToggleTrackedMaterial(row.Material);
        }
    }

    private void OpenTrackedHelp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: TrackedRow row })
        {
            ShowMaterialHelp(row.Advice);
        }
    }

    private void OpenInventoryHelp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: InventoryRow row })
        {
            return;
        }

        MaterialInventoryEntry material = row.Material;
        MaterialAcquisitionAdvice advice = materialAdvisor.Create(new MaterialRequirement(
            material.Id, material.Name, material.Category, material.Count + 1, material.Count));
        ShowMaterialHelp(advice);
    }

    private void AdviceGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AdviceGrid.SelectedItem is AdviceRow row)
        {
            ShowAdviceHelp(row);
        }
    }

    private void CopyAdviceSystem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: AdviceRow row })
        {
            CopySystem(row.SystemName);
        }
    }

    private void OpenAdviceHelp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: AdviceRow row })
        {
            ShowAdviceHelp(row);
        }
    }

    private void CopyWidgetSystem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: WidgetRequirementRow row })
        {
            CopySystem(row.SystemName);
        }
    }

    private void OpenWidgetHelp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: WidgetRequirementRow { Advice: not null } row })
        {
            widgetHelpMaterialId = row.Advice.MaterialId;
            PopulateWidgetHelp(row.Advice);
            EngineeringWidgetPanel.Visibility = Visibility.Collapsed;
            EngineeringWidgetHelpPanel.Visibility = Visibility.Visible;
            Height = 440;
            PositionOverTarget();
        }
    }

    private void PopulateWidgetHelp(MaterialAcquisitionAdvice advice)
    {
        WidgetHelpTitle.Text = Loc.Format("Loc_Material_help_format", advice.MaterialName.ToUpperInvariant());
        WidgetHelpOptions.ItemsSource = BuildHelpOptions(advice);
        SetMaterialWikiButton(WidgetMaterialWikiButton, advice);
    }

    private void CloseWidgetHelp_Click(object sender, RoutedEventArgs e)
    {
        widgetHelpMaterialId = null;
        EngineeringWidgetHelpPanel.Visibility = Visibility.Collapsed;
        EngineeringWidgetPanel.Visibility = Visibility.Visible;
        Height = 340;
        PositionOverTarget();
    }

    private void ShowAdviceHelp(AdviceRow row)
    {
        ShowMaterialHelp(row.Advice);
    }

    private void ShowMaterialHelp(MaterialAcquisitionAdvice advice)
    {
        EngineeringTabs.SelectedIndex = 3;
        AdviceHelpTitle.Text = Loc.Format("Loc_Material_help_format", advice.MaterialName.ToUpperInvariant());
        AdviceHelpOptions.ItemsSource = BuildHelpOptions(advice);
        SetMaterialWikiButton(AdviceMaterialWikiButton, advice);
        AdviceHelpPanel.Visibility = Visibility.Visible;
        AdviceHelpPanel.BringIntoView();
    }

    private void SetMaterialWikiButton(Button button, MaterialAcquisitionAdvice advice)
    {
        button.Tag = MaterialWiki.GetArticleUrl(advice.MaterialId, advice.MaterialName, service.Catalog.Recipes);
        button.IsEnabled = button.Tag is string;
    }

    private static AdviceHelpOption[] BuildHelpOptions(MaterialAcquisitionAdvice advice) =>
        advice.Options
            .OrderBy(option => option.Priority)
            .Select(option => new AdviceHelpOption(
                option.Title,
                option.Instructions,
                option.Destination,
                option.SystemName,
                option.ExternalUrl))
            .ToArray();

    private void CloseAdviceHelp_Click(object sender, RoutedEventArgs e) =>
        AdviceHelpPanel.Visibility = Visibility.Collapsed;

    private void CopyHelpSystem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: AdviceHelpOption option })
        {
            CopySystem(option.SystemName);
        }
    }

    private static void CopySystem(string? systemName)
    {
        if (string.IsNullOrWhiteSpace(systemName))
        {
            return;
        }

        try
        {
            Clipboard.SetText(systemName);
            Logger.Logger.LogUserAction("Engineering acquisition system copied", new { System = systemName });
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning($"Unable to copy Engineering acquisition system: {ex.Message}");
        }
    }

    private void OpenHelpSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: AdviceHelpOption option }
            || string.IsNullOrWhiteSpace(option.ExternalUrl))
        {
            return;
        }

        OpenExternalUrl(option.ExternalUrl);
    }

    private void OpenMaterialWiki_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url })
        {
            OpenExternalUrl(url);
        }
    }

    private void EngineersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EngineersGrid.SelectedItem is not EngineerRow row)
        {
            return;
        }

        selectedEngineer = row;
        EngineerNameText.Text = row.Name;
        EngineerLocationText.Text = row.Location;
        EngineerCurrentStageText.Text = row.CurrentStage;
        EngineerDiscoveryText.Text = row.Definition.Discovery;
        EngineerMeetingText.Text = row.Definition.Meeting;
        EngineerUnlockText.Text = row.Definition.Unlock;
        PopulateEngineerBlueprints(row);
    }

    private void PopulateEngineerBlueprints(EngineerRow engineer)
    {
        string? previousKey = selectedEngineerBlueprint?.Key;
        EngineerBlueprintRow[] rows = service.Catalog.Recipes
            .Where(recipe => !recipe.IsExperimental
                && recipe.Engineers.Any(name => EngineerNameMatches(name, engineer.Name)))
            .GroupBy(RecipeFamilyKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new EngineerBlueprintRow(group.Key, group.OrderBy(recipe => recipe.Grade).ToArray()))
            .OrderBy(row => row.DisplayName)
            .ToArray();

        EngineerBlueprintCombo.ItemsSource = rows;
        EngineerBlueprintCombo.SelectedItem = rows.FirstOrDefault(row => row.Key == previousKey) ?? rows.FirstOrDefault();
        EngineerBlueprintUnavailableText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (rows.Length == 0)
        {
            selectedEngineerBlueprint = null;
            EngineerGradeCombo.ItemsSource = null;
            EngineerGradeMaterialsGrid.ItemsSource = null;
            EngineerPathMaterialsGrid.ItemsSource = null;
        }
    }

    private void EngineerBlueprint_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        selectedEngineerBlueprint = EngineerBlueprintCombo.SelectedItem as EngineerBlueprintRow;
        if (selectedEngineerBlueprint is null)
        {
            return;
        }
        GradeOption[] grades = selectedEngineerBlueprint.Recipes
            .Select(recipe => new GradeOption(recipe, Loc.Format("Loc_Grade_Only_Format", recipe.Grade)))
            .ToArray();
        EngineerGradeCombo.ItemsSource = grades;
        EngineerGradeCombo.SelectedItem = grades.LastOrDefault();
    }

    private void EngineerGrade_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EngineerGradeCombo.SelectedItem is not GradeOption selected || selectedEngineerBlueprint is null)
        {
            return;
        }
        EngineerGradeMaterialsGrid.ItemsSource = BuildIngredientRows([selected.Recipe]);
        EngineerPathMaterialsGrid.ItemsSource = BuildIngredientRows(
            selectedEngineerBlueprint.Recipes.Where(recipe => recipe.Grade <= selected.Recipe.Grade));
    }

    private void PinEngineerGrade_Click(object sender, RoutedEventArgs e)
    {
        if (EngineerGradeCombo.SelectedItem is GradeOption selected)
        {
            service.AddOrIncreaseWishlist(selected.Recipe, 1);
        }
    }

    private void PinEngineerPath_Click(object sender, RoutedEventArgs e)
    {
        if (EngineerGradeCombo.SelectedItem is GradeOption selected && selectedEngineerBlueprint is not null)
        {
            service.AddGradePathToWishlist(
                selectedEngineerBlueprint.Recipes.Where(recipe => recipe.Grade <= selected.Recipe.Grade));
        }
    }

    private static MaterialCountRow[] BuildIngredientRows(IEnumerable<BlueprintRecipe> recipes) =>
        recipes.SelectMany(recipe => recipe.Ingredients)
            .GroupBy(ingredient => ingredient.MaterialId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MaterialCountRow(
                EngineeringLocalization.MaterialName(group.Key, group.First().Name),
                group.Sum(ingredient => ingredient.Count)))
            .OrderBy(row => row.Name)
            .ToArray();

    private static string RecipeFamilyKey(BlueprintRecipe recipe)
    {
        int marker = recipe.Id.LastIndexOf(":G", StringComparison.OrdinalIgnoreCase);
        return marker > 0 ? recipe.Id[..marker] : $"{recipe.ModuleName}|{recipe.BlueprintName}";
    }

    private static bool EngineerNameMatches(string catalogName, string engineerName)
    {
        string left = MaterialName.Normalize(catalogName)
            .Replace("theblaster", string.Empty, StringComparison.OrdinalIgnoreCase);
        string right = MaterialName.Normalize(engineerName)
            .Replace("theblaster", string.Empty, StringComparison.OrdinalIgnoreCase);
        return left.Equals(right, StringComparison.OrdinalIgnoreCase)
            || left.EndsWith(right, StringComparison.OrdinalIgnoreCase)
            || right.EndsWith(left, StringComparison.OrdinalIgnoreCase);
    }

    private void CopyEngineerSystem_Click(object sender, RoutedEventArgs e) =>
        CopySystem(selectedEngineer?.SystemName);

    private void OpenEngineerWiki_Click(object sender, RoutedEventArgs e)
    {
        if (selectedEngineer is not null)
        {
            OpenExternalUrl(selectedEngineer.Definition.WikiUrl);
        }
    }

    private static void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Logger.Warning($"Unable to open Engineering reference link: {ex.Message}");
        }
    }

    private sealed record AdviceRow(
        string Material,
        int Missing,
        string Source,
        string Destination,
        string Instructions,
        string? SystemName,
        MaterialAcquisitionAdvice Advice)
    {
        public bool CanCopySystem => !string.IsNullOrWhiteSpace(SystemName);
    }

    private sealed record AdviceHelpOption(
        string Title,
        string Instructions,
        string Destination,
        string? SystemName,
        string? ExternalUrl)
    {
        public bool CanCopySystem => !string.IsNullOrWhiteSpace(SystemName);
        public bool CanOpenUrl => !string.IsNullOrWhiteSpace(ExternalUrl);
    }

    private sealed record WidgetRequirementRow(
        string Material,
        string Progress,
        string? SystemName,
        MaterialAcquisitionAdvice? Advice)
    {
        public bool CanCopySystem => !string.IsNullOrWhiteSpace(SystemName);
        public bool CanOpenHelp => Advice?.Options.Count > 0;

        public static WidgetRequirementRow Completed(string text) => new(text, string.Empty, null, null);
    }

    private sealed record InventoryRow(MaterialInventoryEntry Material, bool IsTracked)
    {
        public string Name => Material.Name;
        public string CategoryName => Material.CategoryName;
        public int Count => Material.Count;
        public DateTimeOffset? UpdatedUtc => Material.UpdatedUtc;
        public string TrackingLabel => Loc.Get(IsTracked ? "Loc_UNTRACK" : "Loc_TRACK");
    }

    private sealed record EngineerRow(
        EngineerDefinition Definition,
        string CurrentStatus,
        string CurrentStage)
    {
        public string Name => Definition.Name;
        public string EngineerType => Loc.Get(Definition.IsOnFoot ? "Loc_Engineer_type_Odyssey" : "Loc_Engineer_type_Ship");
        public string SystemName => Definition.SystemName;
        public string Location => $"{Definition.SystemName} / {Definition.BodyName} / {Definition.BaseName}";

        public static EngineerRow Create(EngineerDefinition definition, EngineerProgressEntry? progress)
        {
            if (progress is null)
            {
                string empty = Loc.Get("Loc_NO_JOURNAL_PROGRESS");
                return new EngineerRow(definition, Loc.Get("Loc_Empty_Value"), empty);
            }

            string status = progress.Progress.ToLowerInvariant() switch
            {
                "known" => Loc.Get("Loc_Engineer_progress_Known"),
                "invited" => Loc.Get("Loc_Engineer_progress_Invited"),
                "unlocked" => Loc.Get("Loc_Engineer_progress_Unlocked"),
                _ => progress.Progress
            };
            string current = progress.Rank > 0
                ? Loc.Format("Loc_Engineer_progress_rank_format", status, progress.Rank, progress.RankProgress)
                : status;
            return new EngineerRow(definition, status, current);
        }
    }

    private sealed record TrackedRow(
        MaterialInventoryEntry Material,
        int Target,
        MaterialAcquisitionAdvice Advice)
    {
        public string Name => Material.Name;
        public string CategoryName => Material.CategoryName;
        public int Available => Material.Count;
        public int Missing => Math.Max(0, Target - Available);
    }

    private sealed record EngineerBlueprintRow(string Key, IReadOnlyList<BlueprintRecipe> Recipes)
    {
        public string DisplayName => Recipes.Count == 0
            ? Key
            : $"{CoriolisRussianLocalization.Translate(Recipes[0].ModuleName)} · {CoriolisRussianLocalization.Translate(Recipes[0].BlueprintName)}";
    }

    private sealed record GradeOption(BlueprintRecipe Recipe, string Label);
    private sealed record MaterialCountRow(string Name, int Count);
}
