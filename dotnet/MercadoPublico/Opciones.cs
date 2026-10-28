namespace MercadoPublico;

/// <summary>Configuración del cliente. Los tiempos van en milisegundos salvo <see cref="TimeoutSegundos"/>.</summary>
public sealed record Opciones
{
    public Opciones(string ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket)) throw new ArgumentException("El ticket no puede estar vacío.", nameof(ticket));
        Ticket = ticket;
    }

    /// <summary>Ticket de la API (se pide gratis en api.mercadopublico.cl). Nunca lo deje escrito en el código.</summary>
    public string Ticket { get; }

    /// <summary>Base de la API v1 (licitaciones, órdenes de compra, empresas).</summary>
    public string UrlBaseV1 { get; init; } = "https://api.mercadopublico.cl/servicios/v1/publico";

    /// <summary>Base de la API v2 (Compra Ágil).</summary>
    public string UrlBaseV2 { get; init; } = "https://api2.mercadopublico.cl/v2/compra-agil";

    public int TimeoutSegundos { get; init; } = 45;

    /// <summary>Intentos totales por consulta ante errores transitorios (peticiones simultáneas, 429, 5xx, red).</summary>
    public int MaxIntentos { get; init; } = 4;

    /// <summary>Espera base entre reintentos; el intento N espera N veces este valor.</summary>
    public int EsperaReintentoMs { get; init; } = 2000;

    /// <summary>Separación mínima entre dos consultas. Sin ella la API responde "peticiones simultáneas" o 429.</summary>
    public int IntervaloMinimoMs { get; init; } = 1000;

    /// <summary>Sin User-Agent el WAF de la API v2 responde 403.</summary>
    public string UserAgent { get; init; } = "Mozilla/5.0 (compatible; mercado-publico-api/1.0; +https://github.com/saraivaruas/mercado-publico-api)";
}
