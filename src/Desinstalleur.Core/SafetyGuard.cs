namespace Desinstalleur.Core;

/// <summary>
/// Dernier rempart avant toute suppression : refuse tout chemin ou clé de registre
/// qui appartient à Windows ou qui est un dossier « racine » partagé par plusieurs logiciels.
/// Les règles sont volontairement strictes : en cas de doute, on refuse.
/// Les chemins sont traités comme des chaînes Windows (séparateur « \ »)
/// pour que ces règles soient testables sur toutes les plateformes.
/// </summary>
public sealed class SafetyGuard
{
    private readonly HashSet<string> _protectedExact;
    private readonly List<string> _protectedTrees;
    private readonly List<string> _scanRoots;

    /// <param name="protectedExact">Dossiers qui ne peuvent jamais être supprimés eux-mêmes
    /// (ex. « C:\Program Files », « C:\Users\Alex\AppData\Roaming »).</param>
    /// <param name="protectedTrees">Arborescences entièrement interdites (ex. « C:\Windows »).</param>
    /// <param name="scanRoots">Dossiers dans lesquels on cherche des restes ; un reste doit être
    /// strictement à l'intérieur de l'un d'eux, ou suffisamment profond ailleurs.</param>
    public SafetyGuard(IEnumerable<string> protectedExact, IEnumerable<string> protectedTrees, IEnumerable<string> scanRoots)
    {
        _protectedExact = new HashSet<string>(protectedExact.Select(NormalizePath).Where(p => p.Length > 0), StringComparer.Ordinal);
        _protectedTrees = protectedTrees.Select(NormalizePath).Where(p => p.Length > 0).ToList();
        _scanRoots = scanRoots.Select(NormalizePath).Where(p => p.Length > 0).ToList();

        foreach (var root in _scanRoots)
            _protectedExact.Add(root);
    }

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        var p = path.Trim().Trim('"').Replace('/', '\\');
        while (p.Contains(@"\\", StringComparison.Ordinal))
            p = p.Replace(@"\\", @"\", StringComparison.Ordinal);
        return p.TrimEnd('\\').ToLowerInvariant();
    }

    /// <summary>Nombre de segments après la lettre de lecteur (« D:\Jeux\Truc » → 2).</summary>
    public static int Depth(string normalizedPath) =>
        normalizedPath.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length - 1;

    /// <summary>Renvoie null si la suppression est autorisée, sinon la raison du refus.</summary>
    public string? CheckPath(string? path)
    {
        var p = NormalizePath(path);

        if (p.Length < 4 || p.Contains("..", StringComparison.Ordinal))
            return "Chemin invalide.";
        if (!(char.IsLetter(p[0]) && p[1] == ':' && p[2] == '\\'))
            return "Seuls les chemins locaux complets sont acceptés.";
        if (Depth(p) < 1)
            return "Impossible de supprimer la racine d'un lecteur.";
        if (_protectedExact.Contains(p))
            return "Dossier système ou partagé : protégé.";

        foreach (var tree in _protectedTrees)
        {
            if (IsSameOrInside(p, tree))
                return "Ce chemin fait partie de Windows : protégé.";
        }

        // Un dossier protégé ne doit pas se trouver À L'INTÉRIEUR du chemin à supprimer.
        foreach (var exact in _protectedExact)
        {
            if (IsInside(exact, p))
                return "Ce dossier contient un dossier protégé.";
        }
        foreach (var tree in _protectedTrees)
        {
            if (IsInside(tree, p))
                return "Ce dossier contient une partie de Windows.";
        }

        var insideScanRoot = _scanRoots.Any(root => IsInside(p, root));
        if (!insideScanRoot && Depth(p) < 2)
            return "Dossier trop proche de la racine du lecteur : protégé par précaution.";

        return null;
    }

    public bool IsPathDeletable(string? path) => CheckPath(path) is null;

    private static readonly string[] AllowedRegistryPrefixes =
    {
        @"hkey_current_user\software\",
        @"hkey_local_machine\software\",
        @"hkey_local_machine\software\wow6432node\",
    };

    private static readonly string[] UninstallKeyPrefixes =
    {
        @"hkey_current_user\software\microsoft\windows\currentversion\uninstall\",
        @"hkey_local_machine\software\microsoft\windows\currentversion\uninstall\",
        @"hkey_local_machine\software\wow6432node\microsoft\windows\currentversion\uninstall\",
    };

    /// <summary>Éditeurs dont la clé racine ne doit jamais être supprimée.</summary>
    private static readonly HashSet<string> ProtectedVendorKeys = new(StringComparer.Ordinal)
    {
        "microsoft", "classes", "policies", "wow6432node", "windows", "clients", "registeredapplications",
        "intel", "amd", "nvidia corporation", "realtek", "odbc", "khronos",
    };

    /// <summary>Renvoie null si la clé de registre peut être supprimée, sinon la raison du refus.</summary>
    public static string? CheckRegistryKey(string? keyPath)
    {
        var k = NormalizePath(keyPath);
        if (k.StartsWith(@"hkcu\", StringComparison.Ordinal))
            k = "hkey_current_user" + k[4..];
        else if (k.StartsWith(@"hklm\", StringComparison.Ordinal))
            k = "hkey_local_machine" + k[4..];

        if (k.Contains("..", StringComparison.Ordinal))
            return "Clé invalide.";

        // Entrée de désinstallation restée orpheline : autorisée si c'est bien une sous-clé directe.
        foreach (var prefix in UninstallKeyPrefixes)
        {
            if (k.StartsWith(prefix, StringComparison.Ordinal))
            {
                var rest = k[prefix.Length..];
                return rest.Length > 0 && !rest.Contains('\\') ? null : "Clé de désinstallation invalide.";
            }
        }

        var allowedPrefix = AllowedRegistryPrefixes
            .Where(p => k.StartsWith(p, StringComparison.Ordinal))
            .OrderByDescending(p => p.Length)
            .FirstOrDefault();
        if (allowedPrefix is null)
            return "Seules les clés sous « Software » sont autorisées.";

        var segments = k[allowedPrefix.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return "Clé racine protégée.";
        if (ProtectedVendorKeys.Contains(segments[0]))
            return "Clé appartenant à Windows ou à un pilote : protégée.";

        return null;
    }

    private static bool IsInside(string path, string parent) =>
        path.Length > parent.Length + 1 && path.StartsWith(parent + "\\", StringComparison.Ordinal);

    private static bool IsSameOrInside(string path, string parent) => path == parent || IsInside(path, parent);
}
