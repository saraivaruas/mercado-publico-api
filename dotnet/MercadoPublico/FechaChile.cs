namespace MercadoPublico;

/// <summary>
/// La API entiende "hoy" como el día calendario de Chile. Un servidor en UTC pasa a "mañana" por la
/// tarde/noche de Chile y la API rechaza esa fecha, por eso nunca use la hora del servidor.
/// </summary>
public static class FechaChile
{
    private static readonly TimeZoneInfo Zona = Buscar();

    public static DateOnly Hoy() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow, Zona));

    /// <summary>Formato que exige la API: ddmmaaaa.</summary>
    public static string Formato(DateOnly fecha) => fecha.ToString("ddMMyyyy", System.Globalization.CultureInfo.InvariantCulture);

    private static TimeZoneInfo Buscar()
    {
        foreach (var id in new[] { "America/Santiago", "Pacific SA Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        }
        throw new InvalidOperationException("No se encontró la zona horaria de Chile (America/Santiago).");
    }
}
