using System.Text.Json;

namespace MercadoPublico;

/// <summary>
/// Resultado de una consulta. Las fallas de la API NO lanzan excepciones: se devuelven con
/// <see cref="Exito"/> en falso y el mensaje oficial de Mercado Público. Los elementos de
/// <see cref="Items"/> se entregan tal cual los devuelve la API, porque su forma no está garantizada.
/// </summary>
public sealed class Resultado
{
    private Resultado(bool exito, string mensaje, IReadOnlyList<JsonElement> items, int? codigoError, int? http, bool truncado, int? total)
    {
        Exito = exito; Mensaje = mensaje; Items = items; CodigoError = codigoError; Http = http; Truncado = truncado; Total = total;
    }

    public bool Exito { get; }
    public string Mensaje { get; }
    public IReadOnlyList<JsonElement> Items { get; }

    /// <summary>Código de error de la API (ej. 203 ticket inválido, 10500 peticiones simultáneas) o HTTP en Compra Ágil.</summary>
    public int? CodigoError { get; }
    public int? Http { get; }

    /// <summary>Verdadero si el listado es PARCIAL (una página falló o se agotó el tiempo): faltan registros.</summary>
    public bool Truncado { get; }

    /// <summary>Total declarado por la API, si lo informa.</summary>
    public int? Total { get; }

    /// <summary>Primer elemento (útil en consultas por código).</summary>
    public JsonElement? Primero => Items.Count > 0 ? Items[0] : null;

    internal static Resultado Ok(IReadOnlyList<JsonElement> items, int? total = null, bool truncado = false, string mensaje = "Conexión exitosa.", int? http = 200)
        => new(true, mensaje, items, null, http, truncado, total);

    internal static Resultado Error(string mensaje, int? codigoError = null, int? http = null)
        => new(false, mensaje, Array.Empty<JsonElement>(), codigoError, http, false, null);
}
