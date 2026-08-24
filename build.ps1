Set-Location "D:\Projects\EDActivityOverlay_2.0\EDActivityOverlay_2.0"
dotnet build EDActivityOverlay_2.0.sln
Write-Host "Build completed. Press any key to continue..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
