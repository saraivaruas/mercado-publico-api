using System.Text.Json;
using MercadoPublico;
using MercadoPublico.Tests;

if (args.Contains("--vivo")) return Vivo.Ejecutar();

using var srv = new Servidor();
var ok = 0;
var fallas = new List<string>();

void Prueba(string nombre, Func<Task> f)
{
    try { f().GetAwaiter().GetResult(); ok++; Console.WriteLine($"  ✔ {nombre}"); }
    catch (Exception e) { fallas.Add(nombre); Console.WriteLine($"  ✘ {nombre} — {e.Message}"); }
}
void Igual<T>(T esperado, T real, string msg = "")
{
    if (!EqualityComparer<T>.Default.Equals(esperado, real)) throw new Exception($"{(msg == "" ? "" : msg + ": ")}esperado {esperado}, real {real}");
}
Cliente Nuevo(string ticket) => new(new Opciones(ticket + Guid.NewGuid().ToString("N")[..6])
{
    UrlBaseV1 = $"http://127.0.0.1:{srv.Puerto}/v1/publico",
    UrlBaseV2 = $"http://127.0.0.1:{srv.Puerto}/v2/compra-agil",
    TimeoutSegundos = 10, MaxIntentos = 3, EsperaReintentoMs = 1, IntervaloMinimoMs = 0,
});
string? S(JsonElement e, string n) => e.GetProperty(n).GetString();

Console.WriteLine($"Mercado Público — pruebas C# ({Environment.Version})");

Prueba("fecha en formato ddmmaaaa", () => { Igual("05032026", FechaChile.Formato(new DateOnly(2026, 3, 5))); return Task.CompletedTask; });
Prueba("licitaciones por fecha manda ticket y filtros", async () =>
{
    using var c = Nuevo("ok");
    var r = await c.LicitacionesAsync(new Filtros { Fecha = new DateOnly(2026, 3, 5), Estado = "adjudicada", CodigoOrganismo = "7314" });
    Igual(true, r.Exito);
    var q = r.Items[0].GetProperty("q");
    Igual("05032026", S(q, "fecha")); Igual("adjudicada", S(q, "estado")); Igual("7314", S(q, "CodigoOrganismo"));
});
Prueba("reintenta 'peticiones simultáneas' y termina bien", async () =>
{
    using var c = Nuevo("simult");
    var r = await c.LicitacionAsync("1-L1");
    Igual(true, r.Exito, "éxito tras reintentos"); Igual("1-L1", S(r.Primero!.Value, "CodigoExterno"));
});
Prueba("reintenta HTTP 429", async () => { using var c = Nuevo("r429"); Igual(true, (await c.LicitacionesActivasAsync()).Exito); });
Prueba("ticket inválido no se reintenta y devuelve el mensaje oficial", async () =>
{
    using var c = Nuevo("malo");
    var r = await c.LicitacionesActivasAsync();
    Igual(false, r.Exito); Igual(203, r.CodigoError ?? 0); Igual("Ticket no válido.", r.Mensaje);
});
Prueba("sin filtros válidos falla localmente", async () => { using var c = Nuevo("ok"); Igual(false, (await c.LicitacionesAsync(new Filtros())).Exito); });
Prueba("convenio marco filtra solo los códigos -CM<año>", async () =>
{
    using var c = Nuevo("ok");
    var r = await c.OrdenesConvenioMarcoAsync(new DateOnly(2026, 3, 5));
    Igual("2-2-CM26,3-3-CM25", string.Join(",", r.Items.Select(i => S(i, "Codigo"))));
    Igual(true, Cliente.EsConvenioMarco("1002584-358-CM26")); Igual(false, Cliente.EsConvenioMarco("1-1-SE26"));
});
Prueba("compradores y proveedores", async () =>
{
    using var c = Nuevo("ok");
    Igual(2, (await c.CompradoresAsync()).Items.Count);
    Igual("LATAM", S((await c.ProveedoresAsync("89.862.200-2")).Primero!.Value, "NombreEmpresa"));
});
Prueba("proveedor sin resultados es éxito con lista vacía", async () =>
{
    using var c = Nuevo("ok");
    var r = await c.ProveedoresAsync("0-0");
    Igual(true, r.Exito); Igual(0, r.Items.Count);
});
Prueba("Compra Ágil: ticket por cabecera y user-agent (si no, 403)", async () =>
{
    using var c = Nuevo("ok");
    var r = await c.CompraAgilAsync("3754-146-COT26");
    Igual(true, r.Exito); Igual("3754-146-COT26", S(r.Primero!.Value, "codigo"));
});
Prueba("Compra Ágil: código inválido devuelve el mensaje de la API", async () =>
{
    using var c = Nuevo("ok");
    var r = await c.CompraAgilAsync("MALO");
    Igual(false, r.Exito); Igual(400, r.Http ?? 0);
});
Prueba("Compra Ágil: recorre las 16 regiones y deduplica", async () =>
{
    using var c = Nuevo("ok");
    var r = await c.CompraAgilPublicadasAsync();
    Igual(true, r.Exito); Igual(false, r.Truncado);
    Igual(16 * 2 + 1, r.Items.Count, "16 regiones × 2 páginas + 1 repetida única");
    Igual(1, r.Items.Count(i => S(i, "codigo") == "REPETIDA"));
});
Prueba("Compra Ágil: palabra clave viaja con %20 y sin recorrer regiones", async () =>
{
    using var c = Nuevo("ok");
    var r = await c.CompraAgilPublicadasAsync(new OpcionesCompraAgil { PalabraClave = "servicio aseo" });
    Igual("servicio aseo", S(r.Items[0], "q"));
    Igual(true, S(r.Items[0], "raw")!.Contains("q=servicio%20aseo"), "espacio como %20, no +");
    Igual(2, r.Items.Count, "una sola consulta paginada (2 páginas)");
});
Prueba("Compra Ágil: página que falla deja el listado truncado", async () =>
{
    using var c = Nuevo("fallapag");
    var r = await c.CompraAgilPublicadasAsync(new OpcionesCompraAgil { Regiones = new[] { 3 } });
    Igual(true, r.Exito); Igual(true, r.Truncado); Igual(1, r.Items.Count);
});
Prueba("Compra Ágil: el tope de tiempo trunca y conserva lo acumulado", async () =>
{
    using var c = Nuevo("lento");
    var r = await c.CompraAgilPublicadasAsync(new OpcionesCompraAgil { MaxSegundos = 1 });
    Igual(true, r.Truncado); Igual(true, r.Items.Count is > 0 and < 33);
});
Prueba("participantes: licitación adjudicada", () =>
{
    using var d = JsonDocument.Parse("""{"CodigoExterno":"X","Comprador":{"NombreOrganismo":"HOSPITAL","RutUnidad":"61.602.148-6","CodigoOrganismo":"7314"},"Items":{"Listado":[{"Adjudicacion":{"RutProveedor":"78.723.570-0","NombreProveedor":"BARRON SPA"}},{"Adjudicacion":{"RutProveedor":"78.723.570-0","NombreProveedor":"BARRON SPA"}},{"Correlativo":3}]}}""");
    var p = Participantes.De(d.RootElement);
    Igual("HOSPITAL", p.Compradores[0].Nombre); Igual(1, p.Vendedores.Count, "proveedor repetido una sola vez"); Igual(Parte.Vendedor, p.Vendedores[0].Rol);
    return Task.CompletedTask;
});
Prueba("participantes: orden de compra y Compra Ágil", async () =>
{
    using var d = JsonDocument.Parse("""{"Codigo":"1-CM26","Comprador":{"NombreOrganismo":"DEP"},"Proveedor":{"Nombre":"LATAM","RutSucursal":"89.862.200-2"}}""");
    var oc = Participantes.De(d.RootElement);
    Igual("LATAM", oc.Vendedores[0].Nombre); Igual("orden_compra", oc.Compradores[0].Fuente);
    using var c = Nuevo("ok");
    var ca = Participantes.De((await c.CompraAgilAsync("X-1-COT26")).Primero!.Value);
    Igual("MUNI X", ca.Compradores[0].Nombre); Igual("PROV UNO", ca.Vendedores[0].Nombre);
});
Prueba("el ticket vacío se rechaza", () =>
{
    try { _ = new Opciones("  "); throw new Exception("debió lanzar"); } catch (ArgumentException) { }
    return Task.CompletedTask;
});

Console.WriteLine($"\n{ok} pruebas correctas" + (fallas.Count > 0 ? $", {fallas.Count} con falla" : ""));
return fallas.Count > 0 ? 1 : 0;
