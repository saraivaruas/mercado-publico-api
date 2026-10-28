using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace MercadoPublico.Tests;

/// <summary>Servidor de mentira que imita las APIs de Mercado Público. El ticket elige el escenario.</summary>
internal sealed class Servidor : IDisposable
{
    private readonly HttpListener _l = new();
    private readonly ConcurrentDictionary<string, int> _contadores = new();
    public int Puerto { get; }

    public Servidor()
    {
        Puerto = Random.Shared.Next(20000, 29000);
        _l.Prefixes.Add($"http://127.0.0.1:{Puerto}/");
        _l.Start();
        _ = Task.Run(Bucle);
    }

    public void Dispose() => _l.Close();

    private async Task Bucle()
    {
        while (_l.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _l.GetContextAsync(); } catch { return; }
            _ = Task.Run(() => Atender(ctx));
        }
    }

    private int Contar(string clave) => _contadores.AddOrUpdate(clave, 0, (_, n) => n + 1);

    private static void Responder(HttpListenerContext c, int http, string json)
    {
        var b = Encoding.UTF8.GetBytes(json);
        c.Response.StatusCode = http;
        c.Response.ContentType = "application/json";
        c.Response.OutputStream.Write(b, 0, b.Length);
        c.Response.Close();
    }

    private async Task Atender(HttpListenerContext c)
    {
        var path = c.Request.Url!.AbsolutePath;
        var q = System.Web.HttpUtility.ParseQueryString(c.Request.Url.Query);
        if (path.StartsWith("/v2"))
        {
            var ticket = c.Request.Headers["ticket"] ?? "";
            if (ticket == "" || string.IsNullOrEmpty(c.Request.UserAgent)) { Responder(c, 403, "{\"message\":\"Forbidden\"}"); return; }
            if (ticket.StartsWith("lento")) await Task.Delay(150);
            var m = System.Text.RegularExpressions.Regex.Match(path, "^/v2/compra-agil/(.+)$");
            if (m.Success)
            {
                if (m.Groups[1].Value == "MALO") { Responder(c, 400, "{\"success\":\"NOK\",\"payload\":null,\"errors\":[{\"codigo\":\"X\",\"mensaje\":\"Parámetro de ruta 'codigo' inválido.\"}]}"); return; }
                Responder(c, 200, JsonSerializer.Serialize(new { success = "OK", payload = new { codigo = m.Groups[1].Value, institucion = new { organismo_comprador = "MUNI X", rut = "69.041.300-0", unidad_compra = "SALUD", nombre_region = "Región de Coquimbo" }, proveedores_cotizando = new[] { new { razon_social = "PROV UNO", rut = "1-9" } } } }));
                return;
            }
            var region = int.TryParse(q["region"], out var r) ? r : 0;
            var pagina = int.TryParse(q["numero_pagina"], out var p) ? p : 1;
            if (ticket.StartsWith("fallapag") && region == 3 && pagina == 2) { Responder(c, 500, "{\"message\":\"Internal\"}"); return; }
            var items = new List<object> { new { codigo = $"R{region}-P{pagina}-A", q = q["q"], raw = c.Request.Url.Query, nombre = "x" } };
            if (pagina == 1 && region is 1 or 2) items.Add(new { codigo = "REPETIDA", nombre = "y" });
            Responder(c, 200, JsonSerializer.Serialize(new { success = "OK", payload = new { items, paginacion = new { total_paginas = 2, numero_pagina = pagina, tamano_pagina = 50, total_resultados = 100 } } }));
            return;
        }

        var t = q["ticket"] ?? "";
        if (t.StartsWith("malo")) { Responder(c, 203, "{\"Codigo\":203,\"Mensaje\":\"Ticket no válido.\"}"); return; }
        if (t.StartsWith("simult") && Contar(t + path) < 2) { Responder(c, 200, "{\"Codigo\":10500,\"Mensaje\":\"Hemos detectado que existen peticiones simultáneas\"}"); return; }
        if (t.StartsWith("r429") && Contar(t + path) < 1) { Responder(c, 429, "Too Many Requests"); return; }
        if (path.EndsWith("/Empresas/BuscarComprador")) { Responder(c, 200, "{\"Cantidad\":2,\"listaEmpresas\":[{\"CodigoEmpresa\":\"1\",\"NombreEmpresa\":\"A\"},{\"CodigoEmpresa\":\"2\",\"NombreEmpresa\":\"B\"}]}"); return; }
        if (path.EndsWith("/Empresas/BuscarProveedor"))
        {
            if (q["rutempresaproveedor"] == "0-0") { Responder(c, 500, "{\"Codigo\":10200,\"Mensaje\":\"No hay resultados de empresas.\"}"); return; }
            Responder(c, 200, "{\"Cantidad\":1,\"listaEmpresas\":[{\"CodigoEmpresa\":\"25638\",\"NombreEmpresa\":\"LATAM\"}]}");
            return;
        }
        if (path.EndsWith("/ordenesdecompra.json")) { Responder(c, 200, "{\"Cantidad\":4,\"Listado\":[{\"Codigo\":\"1-1-SE26\"},{\"Codigo\":\"2-2-CM26\"},{\"Codigo\":\"3-3-CM25\"},{\"Codigo\":\"4-4-CM2\"}]}"); return; }
        var qs = new Dictionary<string, string?>();
        foreach (var k in q.AllKeys) if (k != null) qs[k] = q[k];
        Responder(c, 200, JsonSerializer.Serialize(new { Cantidad = 1, Listado = new[] { new { CodigoExterno = "1-L1", q = qs } } }));
    }
}
