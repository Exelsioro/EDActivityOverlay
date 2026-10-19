using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using EDActivityOverlay.Utils;

namespace EDActivityOverlay.Services;

internal sealed record UpdateRelease(
    SemanticVersion Version,
    string InstallerName,
    Uri InstallerUri,
    long InstallerSize,
    Uri ChecksumsUri,
    long ChecksumsSize);

/// <summary>Checks public GitHub releases and installs only a SHA-256 verified Inno installer.</summary>
internal sealed class UpdateService
{
    private const string Repository = "Exelsioro/EDActivityOverlay";
    private const long MaxInstallerBytes = 600L * 1024 * 1024;
    private static readonly Uri ReleasesUri = new($"https://api.github.com/repos/{Repository}/releases?per_page=100");
    private static readonly HttpClient Client = CreateClient();
    private readonly SemaphoreSlim gate = new(1, 1);

    internal static UpdateService Instance { get; } = new();
    internal event EventHandler? Changed;
    internal UpdateRelease? AvailableRelease { get; private set; }
    internal string? Error { get; private set; }
    internal bool IsBusy { get; private set; }
    internal SemanticVersion? CurrentVersion =>
        SemanticVersion.TryParse(Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion, out var version) ? version : null;

    private UpdateService() { }

    private void NotifyChanged()
    {
        try { Changed?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Logger.Logger.Error($"Update UI notification failed: {ex}"); }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EDActivityOverlay-UpdateChecker/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    internal async Task CheckAsync(bool includeBeta, CancellationToken cancellationToken = default)
    {
        if (!await gate.WaitAsync(0, cancellationToken)) return;
        try
        {
            IsBusy = true;
            Error = null;
            AvailableRelease = null;
            NotifyChanged();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await Client.GetAsync(ReleasesUri, timeout.Token);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(timeout.Token));
            AvailableRelease = SelectRelease(document.RootElement, CurrentVersion, includeBeta);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Error = ex.Message;
            Logger.Logger.Error($"Update check failed: {ex}");
        }
        finally
        {
            IsBusy = false;
            gate.Release();
            NotifyChanged();
        }
    }

    internal static UpdateRelease? SelectRelease(JsonElement releases, SemanticVersion? current, bool includeBeta)
    {
        if (current is null || releases.ValueKind != JsonValueKind.Array) return null;
        UpdateRelease? best = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!SemanticVersion.TryParse(tag, out var version) || version is null || version.CompareTo(current) <= 0)
                continue;
            if (!includeBeta && (version.IsPrerelease || release.GetProperty("prerelease").GetBoolean())) continue;

            string installerName = $"EDActivityOverlay_Setup_{tag.TrimStart('v')}.exe";
            JsonElement? installer = null, checksums = null;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                string? name = asset.GetProperty("name").GetString();
                if (name == installerName) installer = asset;
                if (name == "SHA256SUMS.txt") checksums = asset;
            }
            if (installer is null || checksums is null ||
                !TryGetReleaseAsset(installer.Value, tag, installerName, MaxInstallerBytes, out var installerUri, out long installerSize) ||
                !TryGetReleaseAsset(checksums.Value, tag, "SHA256SUMS.txt", 100_000, out var checksumsUri, out long checksumsSize))
                continue;
            var candidate = new UpdateRelease(version, installerName, installerUri!, installerSize, checksumsUri!, checksumsSize);
            if (best is null || candidate.Version.CompareTo(best.Version) > 0) best = candidate;
        }
        return best;
    }

    private static bool TryGetReleaseAsset(JsonElement asset, string tag, string name, long maxSize, out Uri? uri, out long size)
    {
        uri = null;
        size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > maxSize || asset.GetProperty("state").GetString() != "uploaded") return false;
        if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out uri)) return false;
        string expectedPath = $"/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(name)}";
        return uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath == expectedPath;
    }

    internal async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        var release = AvailableRelease ?? throw new InvalidOperationException(Loc.Get("Loc_UPDATE_NO_RELEASE"));
        if (!await gate.WaitAsync(0, cancellationToken)) return;
        string? destination = null;
        try
        {
            IsBusy = true;
            Error = null;
            NotifyChanged();

            string checksumText = await DownloadTextAsync(release.ChecksumsUri, release.ChecksumsSize, cancellationToken);
            string expectedHash = ParseChecksum(checksumText, release.InstallerName)
                ?? throw new InvalidDataException(Loc.Get("Loc_UPDATE_CHECKSUM_MISSING"));
            string directory = Path.Combine(Path.GetTempPath(), "EDActivityOverlay", "Updates", release.Version.Value);
            Directory.CreateDirectory(directory);
            destination = Path.Combine(directory, release.InstallerName);
            using (var response = await Client.GetAsync(release.InstallerUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long contentLength && contentLength != release.InstallerSize)
                    throw new InvalidDataException(Loc.Get("Loc_UPDATE_SIZE_MISMATCH"));
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[1024 * 128];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    total += read;
                    if (total > release.InstallerSize || total > MaxInstallerBytes)
                        throw new InvalidDataException(Loc.Get("Loc_UPDATE_SIZE_MISMATCH"));
                    sha.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (total != release.InstallerSize ||
                    !CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(expectedHash), sha.GetHashAndReset()))
                    throw new InvalidDataException(Loc.Get("Loc_UPDATE_HASH_MISMATCH"));
            }

            _ = Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true })
                ?? throw new InvalidOperationException(Loc.Get("Loc_UPDATE_LAUNCH_FAILED"));
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            if (destination is not null && File.Exists(destination))
            {
                try { File.Delete(destination); } catch (IOException) { }
            }
            Error = ex.Message;
            Logger.Logger.Error($"Update installation failed: {ex}");
            throw;
        }
        finally
        {
            IsBusy = false;
            gate.Release();
            NotifyChanged();
        }
    }

    private static async Task<string> DownloadTextAsync(Uri uri, long expectedSize, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length != expectedSize)
            throw new InvalidDataException(Loc.Get("Loc_UPDATE_SIZE_MISMATCH"));
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) != 0)
        {
            if (buffer.Length + read > expectedSize || buffer.Length + read > 100_000)
                throw new InvalidDataException(Loc.Get("Loc_UPDATE_SIZE_MISMATCH"));
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length != expectedSize)
            throw new InvalidDataException(Loc.Get("Loc_UPDATE_SIZE_MISMATCH"));
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('\uFEFF');
    }

    internal static string? ParseChecksum(string content, string filename)
    {
        foreach (Match match in Regex.Matches(content, @"(?m)^([0-9A-Fa-f]{64})[ \t]+\*?([^\r\n]+)\r?$"))
            if (string.Equals(match.Groups[2].Value, filename, StringComparison.Ordinal))
                return match.Groups[1].Value;
        return null;
    }
}
