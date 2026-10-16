using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Desinstalleur.Core;

public enum MatchConfidence
{
    None = 0,
    /// <summary>Ressemblance partielle : proposé, mais décoché par défaut.</summary>
    Low = 1,
    /// <summary>Correspondance exacte du nom : proposé et coché par défaut.</summary>
    High = 2,
}

/// <summary>
/// Compare des noms de dossiers ou de clés de registre au nom d'un logiciel
/// pour repérer les restes laissés après désinstallation.
/// </summary>
public static class NameMatcher
{
    private static readonly Regex Parenthesized = new(@"\([^)]*\)|\[[^\]]*\]", RegexOptions.Compiled);
    private static readonly Regex VersionLike = new(@"\bv?\d+([._]\d+)+\b|\b\d{4}\b", RegexOptions.Compiled);
    private static readonly Regex NonAlnum = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    private static readonly HashSet<string> NoiseWords = new(StringComparer.Ordinal)
    {
        "x64", "x86", "64", "32", "bit", "64bit", "32bit", "amd64", "arm64", "win64", "win32",
        "version", "edition", "setup", "installer", "install", "uninstall", "update",
        "fr", "en", "us", "fr-fr", "en-us", "the", "for", "windows",
    };

    private static readonly HashSet<string> CompanySuffixes = new(StringComparer.Ordinal)
    {
        "inc", "corp", "corporation", "ltd", "llc", "gmbh", "sa", "sas", "sarl", "co", "company",
        "software", "softwares", "technologies", "technology", "studios", "studio", "limited", "ag", "bv",
    };

    /// <summary>
    /// Noms de dossiers génériques qui ne doivent jamais être considérés comme des restes,
    /// même s'ils ressemblent au nom du logiciel.
    /// </summary>
    private static readonly HashSet<string> GenericFolders = new(StringComparer.Ordinal)
    {
        "microsoft", "windows", "commonfiles", "temp", "tmp", "cache", "caches", "packages",
        "programs", "program", "programfiles", "programdata", "users", "public", "default",
        "system", "system32", "syswow64", "drivers", "fonts", "logs", "log", "data", "config",
        "settings", "updates", "update", "installer", "downloads", "documents", "desktop",
        "appdata", "local", "locallow", "roaming", "app", "apps", "application", "applications",
        "tools", "bin", "lib", "shared", "common", "runtime", "runtimes", "plugins", "google",
        "nvidia", "intel", "amd", "realtek", "apple", "adobe", "mozilla", "oracle", "java",
    };

    /// <summary>Nom réduit à ses mots significatifs, en minuscules, sans accents ni versions.</summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var s = RemoveDiacritics(name).ToLowerInvariant();
        s = Parenthesized.Replace(s, " ");
        s = VersionLike.Replace(s, " ");
        s = NonAlnum.Replace(s, " ");

        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                     .Where(w => !NoiseWords.Contains(w));
        return string.Join(' ', words);
    }

    /// <summary>Forme compacte (sans espaces) utilisée pour les comparaisons.</summary>
    public static string Compact(string? name) => Normalize(name).Replace(" ", string.Empty);

    /// <summary>Nom d'éditeur sans suffixes juridiques (« Mozilla Corporation » → « mozilla »).</summary>
    public static string CompactPublisher(string? publisher)
    {
        var words = Normalize(publisher).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                       .Where(w => !CompanySuffixes.Contains(w));
        return string.Concat(words);
    }

    public static bool IsGenericFolderName(string? folderName)
    {
        var c = Compact(folderName);
        return c.Length < 3 || GenericFolders.Contains(c);
    }

    /// <summary>
    /// Indique si <paramref name="candidate"/> (nom de dossier ou de clé) correspond au logiciel.
    /// </summary>
    public static MatchConfidence Match(string? candidate, string? programName, string? publisher)
    {
        if (IsGenericFolderName(candidate))
            return MatchConfidence.None;

        var cand = Compact(candidate);
        var prog = Compact(programName);
        if (prog.Length < 3)
            return MatchConfidence.None;

        if (cand == prog)
            return MatchConfidence.High;

        // « Mozilla Firefox » doit correspondre au dossier « Firefox ».
        var pub = CompactPublisher(publisher);
        if (pub.Length >= 3 && prog.StartsWith(pub, StringComparison.Ordinal))
        {
            var withoutPublisher = prog[pub.Length..];
            if (withoutPublisher.Length >= 3 && cand == withoutPublisher)
                return MatchConfidence.High;
        }

        // Ressemblance partielle : préfixe commun suffisamment long.
        if (cand.Length >= 5 && prog.StartsWith(cand, StringComparison.Ordinal))
            return MatchConfidence.Low;
        if (prog.Length >= 5 && cand.StartsWith(prog, StringComparison.Ordinal))
            return MatchConfidence.Low;

        return MatchConfidence.None;
    }

    /// <summary>Le dossier porte-t-il le nom de l'éditeur (ex. « AppData\Roaming\Mozilla ») ?</summary>
    public static bool IsPublisherFolder(string? folderName, string? publisher)
    {
        var pub = CompactPublisher(publisher);
        if (pub.Length < 3)
            return false;
        var cand = Compact(folderName);
        return cand == pub || cand == Compact(publisher);
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
