param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'

function ReadText([string]$Path) {
    if (-not (Test-Path $Path)) { throw "Missing file: ${Path}" }
    return ([IO.File]::ReadAllText((Resolve-Path $Path).Path)).Replace("`r`n","`n")
}

function WriteText([string]$Path,[string]$Text) {
    [IO.File]::WriteAllText(
        (Resolve-Path $Path).Path,
        $Text.Replace("`r`n","`n"),
        [Text.UTF8Encoding]::new($false))
}

function ReplaceOnce([string]$Text,[string]$Old,[string]$New,[string]$Label) {
    $i = $Text.IndexOf($Old,[StringComparison]::Ordinal)
    if ($i -lt 0) { throw "Block not found: ${Label}" }
    if ($Text.IndexOf($Old,$i+$Old.Length,[StringComparison]::Ordinal) -ge 0) {
        throw "Block duplicated: ${Label}"
    }
    return $Text.Substring(0,$i)+$New+$Text.Substring($i+$Old.Length)
}

$branch = (& git branch --show-current).Trim()
if ($branch -ne 'Update-full-exploration-view') {
    throw "Run on Update-full-exploration-view. Current: ${branch}"
}

$xamlPath='ED_Inara_Overlay\Windows\ActivityWorkspaceOverlayWindow.xaml'
$codePath='ED_Inara_Overlay\Windows\ActivityWorkspaceOverlayWindow.xaml.cs'
$enPath='ED_Inara_Overlay\Resources\Localization.en-US.xaml'
$ruPath='ED_Inara_Overlay\Resources\Localization.ru-RU.xaml'

$xaml=ReadText $xamlPath
$code=ReadText $codePath
$en=ReadText $enPath
$ru=ReadText $ruPath

Write-Host 'Applying exact UI polish against pushed branch state...' -ForegroundColor Cyan

# 1. Move catalog status next to Close.
$xaml=ReplaceOnce $xaml @'
                <Button Grid.Column="1" Width="88" Height="34" Content="{DynamicResource Loc_CLOSE}"
                        Click="CloseExplorationAssistantButton_Click"/>
'@ @'
                <StackPanel Grid.Column="1"
                            Orientation="Horizontal"
                            VerticalAlignment="Top">
                    <TextBlock x:Name="CatalogSourceText"
                               Width="285"
                               Margin="0,0,12,0"
                               TextAlignment="Right"
                               TextWrapping="Wrap"
                               FontSize="9"
                               Foreground="{DynamicResource MutedTextColorBrush}"/>
                    <Button Width="88" Height="34"
                            Content="{DynamicResource Loc_CLOSE}"
                            Click="CloseExplorationAssistantButton_Click"/>
                </StackPanel>
'@ 'header close/status'

# 2. Compact search/filter and remove old right-side status.
$xaml=ReplaceOnce $xaml @'
                <Grid Grid.Row="2" Margin="0,9,0,0">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="280"/>
                        <ColumnDefinition Width="12"/>
                        <ColumnDefinition Width="230"/>
                        <ColumnDefinition Width="12"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <TextBox x:Name="CatalogSearchTextBox" Style="{DynamicResource TextBoxStyle}"
                             ToolTip="{DynamicResource Loc_Exploration_search_hint}"
                             TextChanged="CatalogFilterChanged"/>
                    <ComboBox x:Name="CatalogFilterComboBox" Grid.Column="2" Style="{DynamicResource ComboBoxStyle}"
                              DisplayMemberPath="Label" SelectionChanged="CatalogFilterChanged"/>
                    <TextBlock x:Name="CatalogSourceText" Grid.Column="4" VerticalAlignment="Center"
                               TextAlignment="Right" TextWrapping="Wrap"
                               Foreground="{DynamicResource MutedTextColorBrush}"/>
                </Grid>
'@ @'
                <Grid Grid.Row="2"
                      Margin="0,7,0,0"
                      HorizontalAlignment="Left">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="180"/>
                        <ColumnDefinition Width="8"/>
                        <ColumnDefinition Width="150"/>
                    </Grid.ColumnDefinitions>
                    <TextBox x:Name="CatalogSearchTextBox"
                             Height="27"
                             Padding="6,2"
                             Style="{DynamicResource TextBoxStyle}"
                             ToolTip="{DynamicResource Loc_Exploration_search_hint}"
                             TextChanged="CatalogFilterChanged"/>
                    <ComboBox x:Name="CatalogFilterComboBox"
                              Grid.Column="2"
                              Height="27"
                              Padding="5,1"
                              Style="{DynamicResource ComboBoxStyle}"
                              DisplayMemberPath="Label"
                              SelectionChanged="CatalogFilterChanged"/>
                </Grid>
'@ 'filter row'

# 3. Give sidebar fixed readable width.
$xaml=ReplaceOnce $xaml @'
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="2.6*"/>
                    <ColumnDefinition Width="12"/>
                    <ColumnDefinition Width="1*"/>
                </Grid.ColumnDefinitions>
'@ @'
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="12"/>
                    <ColumnDefinition Width="390"/>
                </Grid.ColumnDefinitions>
'@ 'catalog/sidebar proportions'

# 4. Interest column gets 3 lines and full tooltip.
$xaml=ReplaceOnce $xaml @'
                        <DataGridTemplateColumn Header="{DynamicResource Loc_REASON}" Width="1.6*" MinWidth="135">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding Highlights}" ToolTip="{Binding Highlights}"
                                               Margin="3,2" TextWrapping="Wrap"
                                               TextTrimming="CharacterEllipsis"
                                               MaxHeight="40" LineHeight="17"
                                               VerticalAlignment="Center"/>
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
'@ @'
                        <DataGridTemplateColumn Header="{DynamicResource Loc_EXPLORATION_INTEREST_SHORT}"
                                                Width="1.6*" MinWidth="135">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding Highlights}"
                                               ToolTip="{Binding HighlightsTooltip}"
                                               Margin="3,2"
                                               TextWrapping="Wrap"
                                               TextTrimming="CharacterEllipsis"
                                               MaxHeight="54"
                                               LineHeight="17"
                                               VerticalAlignment="Center"/>
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
'@ 'interest column'

$xaml=$xaml.Replace(
    'Header="{DynamicResource Loc_MAPPING_VALUE}"',
    'Header="{DynamicResource Loc_EXPLORATION_VALUE_SHORT}"')
$xaml=$xaml.Replace(
    'Header="{DynamicResource Loc_DISTANCE_LS}"',
    'Header="{DynamicResource Loc_EXPLORATION_ARRIVAL_SHORT}"')

# 5. Compact POI action labels.
$xaml=$xaml.Replace(
    'Content="{DynamicResource Loc_OPEN_POI_DETAILS}"',
    'Content="{DynamicResource Loc_EXPLORATION_INFO_SHORT}"')
$xaml=$xaml.Replace(
    'Content="{DynamicResource Loc_COPY_POI_SYSTEM}"',
    'Content="{DynamicResource Loc_EXPLORATION_COPY_SHORT}"')

# 6. Compact sidebar buttons.
$xaml=$xaml.Replace(
    'x:Name="DeferSelectedBodyButton" Margin="0,0,7,7"',
    'x:Name="DeferSelectedBodyButton" MinWidth="0" Padding="8,3" Margin="0,0,6,6"')
$xaml=$xaml.Replace(
    'x:Name="ResumeSelectedBodyButton" Margin="0,0,7,7"',
    'x:Name="ResumeSelectedBodyButton" MinWidth="0" Padding="8,3" Margin="0,0,6,6"')
$xaml=$xaml.Replace(
    'x:Name="DssGuideSelectedBodyButton" Margin="0,0,7,7"',
    'x:Name="DssGuideSelectedBodyButton" MinWidth="0" Padding="8,3" Margin="0,0,6,6"')
$xaml=$xaml.Replace(
    'x:Name="BookmarkSelectedBodyButton" Margin="0,0,7,7"',
    'x:Name="BookmarkSelectedBodyButton" Width="34" Padding="5,3" Margin="0,0,6,6"')
$xaml=$xaml.Replace(
    'x:Name="CopySelectedBodyButton" Margin="0,0,0,7"',
    'x:Name="CopySelectedBodyButton" MinWidth="0" Padding="8,3" Margin="0,0,0,6"')

# 7. Catalog row now carries compact text + full tooltip.
$code=ReplaceOnce $code @'
    private sealed record CatalogRow(
        ExplorationCatalogBody Body,
        string Name,
        string RowMarker,
        string Type,
        string Highlights,
        string Distance,
'@ @'
    private sealed record CatalogRow(
        ExplorationCatalogBody Body,
        string Name,
        string RowMarker,
        string Type,
        string Highlights,
        string HighlightsTooltip,
        string Distance,
'@ 'CatalogRow'

$code=ReplaceOnce $code @'
        string.IsNullOrWhiteSpace(body.Subtype)
            ? body.Type
            : body.Subtype,
        BuildHighlightText(body),
        Loc.Format(
'@ @'
        string.IsNullOrWhiteSpace(body.Subtype)
            ? body.Type
            : body.Subtype,
        BuildCompactHighlightText(body),
        BuildHighlightText(body),
        Loc.Format(
'@ 'ToCatalogRow'

# Sidebar should keep full localized explanation, not compact tokens.
$code=$code.Replace(
    '        SelectedBodyReasonText.Text = row.Highlights;',
    '        SelectedBodyReasonText.Text = row.HighlightsTooltip;')

$helper=@'
    private static string BuildCompactHighlightText(
        ExplorationCatalogBody body)
    {
        var values = new List<string>();

        void Add(
            ExplorationBodyHighlights flag,
            string label)
        {
            if (body.Highlights.HasFlag(flag))
            {
                values.Add(label);
            }
        }

        Add(ExplorationBodyHighlights.EarthLike, "ELW");
        Add(ExplorationBodyHighlights.WaterWorld, "WW");
        Add(ExplorationBodyHighlights.AmmoniaWorld, "AW");
        Add(ExplorationBodyHighlights.Terraformable, "TERRAFORMABLE");
        Add(ExplorationBodyHighlights.Biological, "BIO");
        Add(ExplorationBodyHighlights.Valuable, "HIGH VALUE");
        Add(ExplorationBodyHighlights.NeutronStar, "NEUTRON");
        Add(ExplorationBodyHighlights.BlackHole, "BLACK HOLE");

        return values.Count == 0
            ? "—"
            : string.Join(
                Environment.NewLine,
                values.Take(3));
    }

'@

if(-not $code.Contains('private static string BuildCompactHighlightText(')) {
    $anchor='    private static string BuildHighlightText(ExplorationCatalogBody body)'
    $i=$code.IndexOf($anchor,[StringComparison]::Ordinal)
    if($i -lt 0){throw 'BuildHighlightText anchor missing'}
    $code=$code.Substring(0,$i)+$helper+$code.Substring($i)
}

# 8. Add localized short labels.
$enInsert=@'
    <sys:String x:Key="Loc_EXPLORATION_INTEREST_SHORT">INTEREST</sys:String>
    <sys:String x:Key="Loc_EXPLORATION_VALUE_SHORT">VALUE</sys:String>
    <sys:String x:Key="Loc_EXPLORATION_ARRIVAL_SHORT">ARRIVAL</sys:String>
    <sys:String x:Key="Loc_EXPLORATION_INFO_SHORT">INFO</sys:String>
    <sys:String x:Key="Loc_EXPLORATION_COPY_SHORT">COPY</sys:String>
'@

$ruInsert=@'
    <sys:String x:Key="Loc_EXPLORATION_INTEREST_SHORT">ИНТЕРЕС</sys:String>
    <sys:String x:Key="Loc_EXPLORATION_VALUE_SHORT">ЦЕННОСТЬ</sys:String>
    <sys:String x:Key="Loc_EXPLORATION_ARRIVAL_SHORT">ОТ ВХОДА</sys:String>
    <sys:String x:Key="Loc_EXPLORATION_INFO_SHORT">ИНФО</sys:String>
    <sys:String x:Key="Loc_EXPLORATION_COPY_SHORT">КОПИРОВАТЬ</sys:String>
'@

if(-not $en.Contains('Loc_EXPLORATION_INTEREST_SHORT')) {
    $en=$en.Replace('</ResourceDictionary>',$enInsert+"`n</ResourceDictionary>")
}
if(-not $ru.Contains('Loc_EXPLORATION_INTEREST_SHORT')) {
    $ru=$ru.Replace('</ResourceDictionary>',$ruInsert+"`n</ResourceDictionary>")
}

WriteText $xamlPath $xaml
WriteText $codePath $code
WriteText $enPath $en
WriteText $ruPath $ru

& git diff --check
if($LASTEXITCODE -ne 0){throw 'git diff --check failed'}

Write-Host ''
Write-Host 'Changed files:' -ForegroundColor Cyan
& git status --short

if(-not $SkipBuild){
    Write-Host ''
    Write-Host 'Building...' -ForegroundColor Cyan
    & dotnet build '.\ED_Inara_Overlay\ED_Inara_Overlay.csproj' -c Debug
    if($LASTEXITCODE -ne 0){throw 'build failed'}

    Write-Host ''
    Write-Host 'Testing...' -ForegroundColor Cyan
    & dotnet test '.\Testing\ED_Inara_Overlay.LayoutTests\ED_Inara_Overlay.LayoutTests.csproj' -c Debug
    if($LASTEXITCODE -ne 0){throw 'tests failed'}
}

Write-Host ''
Write-Host 'Exploration UI polish completed.' -ForegroundColor Green
