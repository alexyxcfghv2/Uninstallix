using System.Globalization;

namespace Desinstalleur.Core;

public static class ValueParsers
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private static readonly string[] DateFormats =
    {
        "yyyyMMdd", "yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "MM/dd/yyyy", "d/M/yyyy", "M/d/yyyy", "dd.MM.yyyy",
    };

    /// <summary>
    /// Lit la valeur « InstallDate » du registre. Le format officiel est « aaaammjj »,
    /// mais beaucoup d'installeurs écrivent autre chose ; les dates absurdes sont ignorées.
    /// </summary>
    public static DateTime? ParseInstallDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTime.TryParseExact(value.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && date.Year >= 1995 && date <= DateTime.Today.AddDays(1))
        {
            return date;
        }

        return null;
    }

    /// <summary>« EstimatedSize » est exprimé en kilo-octets.</summary>
    public static long? EstimatedSizeToBytes(object? value)
    {
        long kb = value switch
        {
            int i => unchecked((uint)i),
            long l => l,
            string s when long.TryParse(s, out var parsed) => parsed,
            _ => 0,
        };
        return kb > 0 ? kb * 1024 : null;
    }

    /// <summary>Unités anglaises (B, KB, MB…) et point décimal au lieu de Ko, Mo… et de la virgule.</summary>
    public static bool EnglishUnits { get; set; }

    public static string FormatSize(long? bytes)
    {
        if (bytes is null or <= 0)
            return "—";

        string[] units = EnglishUnits ? new[] { "B", "KB", "MB", "GB", "TB" } : new[] { "o", "Ko", "Mo", "Go", "To" };
        double size = bytes.Value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        var format = unit == 0 || size >= 100 ? "0" : "0.#";
        return size.ToString(format, EnglishUnits ? CultureInfo.InvariantCulture : French) + " " + units[unit];
    }
}
