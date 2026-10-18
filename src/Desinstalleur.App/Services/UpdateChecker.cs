using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Desinstalleur.App.Services;

/// <summary>Une nouvelle version publiée sur GitHub.</summary>
public sealed record UpdateInfo(Version Version, string Title, string Notes, string DownloadUrl, long Size, string? Sha256, string PageUrl);

/// <summary>
/// Vérifie les nouvelles versions publiées sur GitHub (dépôt public, sans compte),
/// puis télécharge et lance l'installateur si l'utilisateur le demande.
/// </summary>
public static class UpdateChecker
{
    public const string Repository = "alexyxcfghv2/Uninstallix";
    private const string SetupAssetName = "Uninstallix-Setup.exe";
    private const string PortableAssetName = "Uninstallix-Portable.exe";

    /// <summary>En mode portable on télécharge l'exe portable (remplacé sur place), sinon l'installateur.</summary>
    private static string AssetName => PortableMode.IsActive ? PortableAssetName : SetupAssetName;

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Uninstallix", SelfInstaller.Version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static Version CurrentVersion => Version.TryParse(SelfInstaller.Version, out var v) ? v : new Version(0, 0, 0);

    /// <summary>Renvoie la nouvelle version si elle est plus récente, sinon null. Lève une exception si GitHub est injoignable.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancel = default)
    {
        using var response = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", cancel);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null; // aucune version publiée pour l'instant
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        var root = json.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version) || version <= CurrentVersion)
            return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase))
                continue;
            string? sha = null;
            if (asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } d &&
                d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                sha = d[7..];
            return new UpdateInfo(
                version,
                root.TryGetProperty("name", out var name) && !string.IsNullOrWhiteSpace(name.GetString()) ? name.GetString()! : $"Uninstallix {version}",
                NotesForLanguage(root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty),
                asset.GetProperty("browser_download_url").GetString()!,
                asset.GetProperty("size").GetInt64(),
                sha,
                root.GetProperty("html_url").GetString() ?? $"https://github.com/{Repository}/releases");
        }
        return null; // version publiée sans le fichier attendu : on ignore
    }

    /// <summary>
    /// Les notes de version sont en anglais, puis en français après la ligne « &lt;!-- fr --&gt; » :
    /// on ne garde que la partie de la langue de l'interface.
    /// </summary>
    private static string NotesForLanguage(string body)
    {
        const string marker = "<!-- fr -->";
        var index = body.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return body;
        return (L.IsEnglish ? body[..index] : body[(index + marker.Length)..]).Trim();
    }

    /// <summary>Télécharge l'installateur ou l'exe portable (vérifié par SHA-256 quand GitHub le fournit) et renvoie son chemin.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double> progress, CancellationToken cancel = default)
    {
        var target = Path.Combine(Path.GetTempPath(), PortableMode.IsActive ? $"Uninstallix-Portable-{update.Version}.exe" : $"Uninstallix-Setup-{update.Version}.exe");
        using (var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? update.Size;
            await using var source = await response.Content.ReadAsStreamAsync(cancel);
            await using var file = File.Create(target);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancel)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancel);
                done += read;
                if (total > 0)
                    progress.Report(done * 100.0 / total);
            }
        }

        if (update.Sha256 is not null)
        {
            await using var stream = File.OpenRead(target);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancel));
            if (!hash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(target);
                throw new InvalidOperationException(L.T("Le fichier téléchargé est abîmé (empreinte incorrecte). Réessayez plus tard."));
            }
        }
        Journal.Write($"Mise à jour {update.Version} téléchargée : {target}");
        return target;
    }

    /// <summary>
    /// Applique la mise à jour téléchargée : lance l'installateur (il ferme Uninstallix et le met à jour),
    /// ou, en mode portable, remplace l'exe sur place et lance la nouvelle version. L'appelant ferme ensuite l'appli.
    /// </summary>
    public static void RunInstaller(string path)
    {
        if (PortableMode.IsActive)
            PortableMode.ReplaceAndRestart(path);
        else
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
