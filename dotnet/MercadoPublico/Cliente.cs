using System.Diagnostics;
using System.Text.Json;

namespace MercadoPublico;

/// <summary>Filtros de licitaciones y órdenes de compra. Con <see cref="Fecha"/> el listado trae solo código, nombre y estado.</summary>
public sealed record Filtros
{
    /// <summary>Día calendario de Chile (ver <see cref="FechaChile.Hoy"/>).</summary>
    public DateOnly? Fecha { get; init; }
    /// <summary>Licitaciones: activas, publicada, cerrada, desierta, adjudicada, revocada, suspendida, todos. Órdenes: enviadaproveedor, aceptada, cancelada, recepcionconforme...</summary>
    public string? Estado { get; init; }
    public string? Codigo { get; init; }
    public string? CodigoOrganismo { get; init; }
    public string? CodigoProveedor { get; init; }
}

/// <summary>Opciones del listado de Compras Ágiles publicadas.</summary>
public sealed record OpcionesCompraAgil
{
    /// <summary>Filtro de texto de la API (<c>q</c>). Poco fiable a nivel nacional (responde 504): filtre localmente si puede.</summary>
    public string? PalabraClave { get; init; }
    /// <summary>Códigos de región 1 a 16. Vacío = recorrer las 16 y deduplicar.</summary>
    public IReadOnlyList<int>? Regiones { get; init; }
    /// <summary>Tope del recorrido en segundos; al agotarse devuelve lo acumulado con <see cref="Resultado.Truncado"/>.</summary>
    public int? MaxSegundos { get; init; }
}

/// <summary>
/// Cliente de las APIs públicas de Mercado Público (ChileCompra). Mercado Público es la plataforma de compras de la
/// Dirección ChileCompra: este cliente cubre sus dos APIs con el mismo ticket.
/// v1 (api.mercadopublico.cl): licitaciones, órdenes de compra (incluye Convenio Marco), compradores y proveedores.
/// v2 (api2.mercadopublico.cl): Compra Ágil.
/// La API pide ~1 consulta por segundo (si no, "peticiones simultáneas" o 429): el cliente espacia y reintenta.
/// Compra Ágil solo pagina hasta 10.000 resultados por consulta y hay más publicadas, por eso se recorre por región.
/// </summary>
public sealed class Cliente : IDisposable
{
    private const int Regiones = 16;
    private const int TamanoPagina = 50;

    private readonly Opciones _o;
    private readonly HttpClient _http;
    private readonly bool _poseeHttp;
    private readonly SemaphoreSlim _turno = new(1, 1);
    private long _ultima;

    public Cliente(Opciones opciones, HttpClient? http = null)
    {
        if (opciones.MaxIntentos < 1) throw new ArgumentException("MaxIntentos debe ser al menos 1.", nameof(opciones));
        _o = opciones;
        _poseeHttp = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(opciones.TimeoutSegundos) };
    }

    public void Dispose()
    {
        if (_poseeHttp) _http.Dispose();
        _turno.Dispose();
    }

    // ───────────────────────── Licitaciones ─────────────────────────

    public Task<Resultado> LicitacionesAsync(Filtros filtros, CancellationToken ct = default) => V1("licitaciones.json", filtros, "Listado", ct);

    /// <summary>Detalle completo de una licitación (ej. "1001-18-LE26"): comprador, ítems y adjudicación.</summary>
    public Task<Resultado> LicitacionAsync(string codigo, CancellationToken ct = default) => V1("licitaciones.json", new Filtros { Codigo = codigo }, "Listado", ct);

    /// <summary>Todas las licitaciones activas hoy (unas 5.000).</summary>
    public Task<Resultado> LicitacionesActivasAsync(CancellationToken ct = default) => V1("licitaciones.json", new Filtros { Estado = "activas" }, "Listado", ct);

    // ───────────── Órdenes de compra y Convenio Marco ─────────────

    public Task<Resultado> OrdenesCompraAsync(Filtros filtros, CancellationToken ct = default) => V1("ordenesdecompra.json", filtros, "Listado", ct);

    /// <summary>Detalle completo de una orden de compra: comprador, proveedor, ítems y montos.</summary>
    public Task<Resultado> OrdenCompraAsync(string codigo, CancellationToken ct = default) => V1("ordenesdecompra.json", new Filtros { Codigo = codigo }, "Listado", ct);

    /// <summary>Órdenes de Convenio Marco de un día (código con sufijo "-CM&lt;año&gt;"): ~2.000 de las ~18.000 del día.</summary>
    public async Task<Resultado> OrdenesConvenioMarcoAsync(DateOnly fecha, Filtros? filtros = null, CancellationToken ct = default)
    {
        var res = await OrdenesCompraAsync((filtros ?? new Filtros()) with { Fecha = fecha }, ct).ConfigureAwait(false);
        if (!res.Exito) return res;
        var cm = res.Items.Where(o => EsConvenioMarco(Texto(o, "Codigo") ?? "")).ToList();
        return Resultado.Ok(cm, cm.Count, false, res.Mensaje, res.Http);
    }

    public static bool EsConvenioMarco(string codigoOrden)
        => System.Text.RegularExpressions.Regex.IsMatch(codigoOrden, @"-CM\d{2}$");

    // ───────────────── Quién compra / quién vende ─────────────────

    /// <summary>Los ~900 organismos compradores del Estado (CodigoEmpresa, NombreEmpresa).</summary>
    public Task<Resultado> CompradoresAsync(CancellationToken ct = default) => V1("Empresas/BuscarComprador", null, "listaEmpresas", ct);

    /// <summary>Busca un proveedor por RUT (ej. "89.862.200-2"). Sin coincidencia: éxito con lista vacía.</summary>
    public Task<Resultado> ProveedoresAsync(string rut, CancellationToken ct = default)
        => V1("Empresas/BuscarProveedor", null, "listaEmpresas", ct, ("rutempresaproveedor", rut));

    // ─────────────────────── Compra Ágil (v2) ───────────────────────

    /// <summary>Detalle de una Compra Ágil (ej. "3754-146-COT26"): institución, productos, proveedores cotizando.</summary>
    public async Task<Resultado> CompraAgilAsync(string codigo, CancellationToken ct = default)
    {
        var (res, crudo) = await Ejecutar(_o.UrlBaseV2 + "/" + Uri.EscapeDataString(codigo), v2: true, "Listado", ct).ConfigureAwait(false);
        if (!res.Exito) return res;
        var payload = crudo is { } c && c.TryGetProperty("payload", out var p) && p.ValueKind == JsonValueKind.Object ? new[] { p.Clone() } : Array.Empty<JsonElement>();
        return Resultado.Ok(payload, null, false, res.Mensaje, res.Http);
    }

    /// <summary>
    /// Compras Ágiles publicadas (recibiendo cotizaciones). Sin palabra clave ni regiones recorre las 16 regiones y
    /// deduplica, porque una consulta nacional solo pagina hasta 10.000 resultados.
    /// </summary>
    public async Task<Resultado> CompraAgilPublicadasAsync(OpcionesCompraAgil? opciones = null, CancellationToken ct = default)
    {
        opciones ??= new OpcionesCompraAgil();
        var palabra = (opciones.PalabraClave ?? "").Trim();
        var regiones = (opciones.Regiones ?? Array.Empty<int>()).Distinct().ToList();
        var reloj = Stopwatch.StartNew();
        double? limite = opciones.MaxSegundos;

        if (palabra.Length > 0 || regiones.Count > 0) return await Recorrer(palabra, regiones, reloj, limite, ct).ConfigureAwait(false);

        var porCodigo = new Dictionary<string, JsonElement>();
        var truncado = false;
        var alguna = false;
        Resultado? ultimoError = null;
        for (var region = 1; region <= Regiones; region++)
        {
            if (limite is { } l && reloj.Elapsed.TotalSeconds > l) { truncado = true; break; }
            var res = await Recorrer("", new List<int> { region }, reloj, limite, ct).ConfigureAwait(false);
            if (!res.Exito) { truncado = true; ultimoError = res; continue; }
            alguna = true;
            truncado |= res.Truncado;
            foreach (var item in res.Items) porCodigo[Texto(item, "codigo") ?? $"sin-codigo-{porCodigo.Count}"] = item;
        }
        if (!alguna) return ultimoError ?? Resultado.Error("Se agotó el tiempo antes de consultar la primera región.");
        var items = porCodigo.Values.ToList();
        return Resultado.Ok(items, items.Count, truncado, truncado ? "Listado parcial: alguna región falló o se agotó el tiempo." : "Conexión exitosa.");
    }

    // ─────────────────────────── Internos ───────────────────────────

    private async Task<Resultado> Recorrer(string palabra, List<int> regiones, Stopwatch reloj, double? limite, CancellationToken ct)
    {
        var items = new List<JsonElement>();
        int? total = null;
        var pagina = 1;
        var paginas = 1;
        do
        {
            if (limite is { } l && reloj.Elapsed.TotalSeconds > l)
                return Resultado.Ok(items, total, true, $"Listado parcial: se agotó el tiempo en la página {pagina} de {paginas}.");
            var q = new List<(string, string)> { ("estado", "publicada"), ("tamano_pagina", TamanoPagina.ToString()), ("numero_pagina", pagina.ToString()) };
            if (palabra.Length > 0) q.Add(("q", palabra));
            if (regiones.Count > 0) q.Add(("region", string.Join(",", regiones)));
            var (res, crudo) = await Ejecutar(_o.UrlBaseV2 + "?" + Query(q), v2: true, "Listado", ct).ConfigureAwait(false);
            if (!res.Exito)
                return items.Count > 0 ? Resultado.Ok(items, total, true, $"Listado parcial: falló la página {pagina} de {paginas} ({res.Mensaje}).") : res;
            items.AddRange(res.Items);
            if (crudo is { } c && c.TryGetProperty("payload", out var pl) && pl.TryGetProperty("paginacion", out var pg))
            {
                if (pg.TryGetProperty("total_paginas", out var tp) && tp.TryGetInt32(out var np)) paginas = np;
                if (pg.TryGetProperty("total_resultados", out var tr) && tr.TryGetInt32(out var nt)) total = nt;
            }
            pagina++;
        } while (pagina <= paginas);
        return Resultado.Ok(items, total);
    }

    private async Task<Resultado> V1(string ruta, Filtros? f, string clave, CancellationToken ct, params (string, string)[] extra)
    {
        var q = new List<(string, string)> { ("ticket", _o.Ticket) };
        if (f is not null)
        {
            if (f.Fecha is { } fecha) q.Add(("fecha", FechaChile.Formato(fecha)));
            if (!string.IsNullOrWhiteSpace(f.Estado)) q.Add(("estado", f.Estado));
            if (!string.IsNullOrWhiteSpace(f.Codigo)) q.Add(("codigo", f.Codigo));
            if (!string.IsNullOrWhiteSpace(f.CodigoOrganismo)) q.Add(("CodigoOrganismo", f.CodigoOrganismo));
            if (!string.IsNullOrWhiteSpace(f.CodigoProveedor)) q.Add(("CodigoProveedor", f.CodigoProveedor));
            if (q.Count == 1) return Resultado.Error("Indique al menos un filtro válido: fecha, estado o codigo.");
        }
        q.AddRange(extra);
        var url = _o.UrlBaseV1.TrimEnd('/') + "/" + ruta + "?" + Query(q);
        return (await Ejecutar(url, v2: false, clave, ct).ConfigureAwait(false)).Item1;
    }

    private static string Query(IEnumerable<(string Clave, string Valor)> q)
        => string.Join("&", q.Select(p => Uri.EscapeDataString(p.Clave) + "=" + Uri.EscapeDataString(p.Valor)));

    private async Task<(Resultado, JsonElement?)> Ejecutar(string url, bool v2, string clave, CancellationToken ct)
    {
        for (var intento = 1; ; intento++)
        {
            var (http, cuerpo) = await Get(url, v2, ct).ConfigureAwait(false);
            var (res, crudo, transitorio) = v2 ? InterpretarV2(http, cuerpo) : InterpretarV1(http, cuerpo, clave);
            if (!transitorio || intento >= _o.MaxIntentos) return (res, crudo);
            await Task.Delay(intento * _o.EsperaReintentoMs, ct).ConfigureAwait(false);
        }
    }

    private async Task<(int, string)> Get(string url, bool v2, CancellationToken ct)
    {
        await _turno.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var falta = _o.IntervaloMinimoMs - (Environment.TickCount64 - _ultima);
            if (_ultima != 0 && falta > 0) await Task.Delay((int)falta, ct).ConfigureAwait(false);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", _o.UserAgent);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            if (v2) req.Headers.TryAddWithoutValidation("ticket", _o.Ticket);
            try
            {
                using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                return (0, "");
            }
            finally { _ultima = Environment.TickCount64; }
        }
        finally { _turno.Release(); }
    }

    private static (Resultado, JsonElement?, bool) InterpretarV1(int http, string cuerpo, string clave)
    {
        if (cuerpo.Length == 0) return (Resultado.Error("No fue posible contactar a Mercado Público. Verifique la conectividad.", null, http == 0 ? null : http), null, true);
        if (!TryParse(cuerpo, out var datos) || datos.ValueKind != JsonValueKind.Object)
            return (Resultado.Error($"Respuesta inesperada de Mercado Público (HTTP {http}).", null, http), null, http == 429 || http >= 500);
        if (datos.TryGetProperty(clave, out var lista))
        {
            var items = lista.ValueKind == JsonValueKind.Array ? lista.EnumerateArray().Select(e => e.Clone()).ToList() : new List<JsonElement>();
            int? total = datos.TryGetProperty("Cantidad", out var c) && c.TryGetInt32(out var n) ? n : null;
            return (Resultado.Ok(items, total, false, "Conexión exitosa.", http), datos, false);
        }
        int? codigo = datos.TryGetProperty("Codigo", out var cod) && cod.TryGetInt32(out var k) ? k : null;
        var mensaje = Texto(datos, "Mensaje") ?? "Mercado Público rechazó la solicitud.";
        // 10200 = "No hay resultados": la consulta fue válida, simplemente no hay datos.
        if (codigo == 10200) return (Resultado.Ok(Array.Empty<JsonElement>(), 0, false, mensaje, http), datos, false);
        var transitorio = codigo == 10500 || http == 429 || mensaje.Contains("simult", StringComparison.OrdinalIgnoreCase);
        return (Resultado.Error(mensaje, codigo, http), datos, transitorio);
    }

    private static (Resultado, JsonElement?, bool) InterpretarV2(int http, string cuerpo)
    {
        if (cuerpo.Length == 0) return (Resultado.Error("No fue posible contactar la API de Compra Ágil.", null, http == 0 ? null : http), null, true);
        var transitorio = http is 408 or 429 or >= 500;
        if (!TryParse(cuerpo, out var datos) || datos.ValueKind != JsonValueKind.Object)
            return (Resultado.Error($"Respuesta inesperada de la API de Compra Ágil (HTTP {http}).", null, http), null, transitorio);
        if (Texto(datos, "success") == "OK")
        {
            var items = datos.TryGetProperty("payload", out var p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty("items", out var i) && i.ValueKind == JsonValueKind.Array
                ? i.EnumerateArray().Select(e => e.Clone()).ToList() : new List<JsonElement>();
            return (Resultado.Ok(items, null, false, "Conexión exitosa.", http), datos, false);
        }
        string? mensaje = null;
        if (datos.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0) mensaje = Texto(errs[0], "mensaje");
        mensaje ??= Texto(datos, "message") ?? "La API de Compra Ágil rechazó la solicitud.";
        return (Resultado.Error(mensaje, http == 0 ? null : http, http), datos, transitorio);
    }

    private static bool TryParse(string cuerpo, out JsonElement raiz)
    {
        try { using var doc = JsonDocument.Parse(cuerpo); raiz = doc.RootElement.Clone(); return true; }
        catch (JsonException) { raiz = default; return false; }
    }

    private static string? Texto(JsonElement e, string nombre)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
