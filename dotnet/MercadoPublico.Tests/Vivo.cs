using MercadoPublico;

namespace MercadoPublico.Tests;

/// <summary>Prueba VIVA contra la API real (solo lectura). Requiere MERCADOPUBLICO_TICKET: dotnet run --project MercadoPublico.Tests -- --vivo</summary>
internal static class Vivo
{
    public static int Ejecutar()
    {
        var ticket = Environment.GetEnvironmentVariable("MERCADOPUBLICO_TICKET");
        if (string.IsNullOrEmpty(ticket)) { Console.Error.WriteLine("Defina MERCADOPUBLICO_TICKET."); return 2; }
        using var c = new Cliente(new Opciones(ticket));
        var fallas = 0;
        void Ver(string n, bool ok, string detalle = "") { Console.WriteLine($"  {(ok ? "✔" : "✘")} {n}{(detalle == "" ? "" : " — " + detalle)}"); if (!ok) fallas++; }
        string? S(System.Text.Json.JsonElement e, string k) => e.TryGetProperty(k, out var v) ? v.GetString() : null;

        Console.WriteLine("Mercado Público — prueba VIVA (C#)");
        var r = c.LicitacionesActivasAsync().GetAwaiter().GetResult();
        Ver("licitaciones activas", r.Exito && r.Items.Count > 1000, $"{r.Items.Count} licitaciones");

        r = c.LicitacionAsync("2107-152-LE26").GetAwaiter().GetResult();
        var p = r.Exito ? Participantes.De(r.Primero!.Value) : new QuienCompraQuienVende(Array.Empty<Parte>(), Array.Empty<Parte>());
        Ver("licitación adjudicada: quién compra y quién vende", p.Compradores.Count > 0 && p.Vendedores.Count > 0, $"{p.Compradores.FirstOrDefault()?.Nombre} ← {p.Vendedores.FirstOrDefault()?.Nombre}");

        r = c.OrdenesConvenioMarcoAsync(FechaChile.Hoy().AddDays(-1)).GetAwaiter().GetResult();
        Ver("convenios marco de ayer", r.Exito && r.Items.Count > 500, $"{r.Items.Count} órdenes CM");

        r = c.OrdenCompraAsync("1002584-358-CM26").GetAwaiter().GetResult();
        p = r.Exito ? Participantes.De(r.Primero!.Value) : new QuienCompraQuienVende(Array.Empty<Parte>(), Array.Empty<Parte>());
        Ver("orden de compra: quién compra y quién vende", p.Vendedores.FirstOrDefault()?.Rut == "89.862.200-2", $"{p.Compradores.FirstOrDefault()?.Nombre} ← {p.Vendedores.FirstOrDefault()?.Nombre}");

        r = c.CompradoresAsync().GetAwaiter().GetResult();
        Ver("compradores (organismos)", r.Exito && r.Items.Count > 800, $"{r.Items.Count} organismos");
        r = c.ProveedoresAsync("89.862.200-2").GetAwaiter().GetResult();
        Ver("proveedor por RUT", r.Exito && r.Primero is { } f && S(f, "CodigoEmpresa") == "25638", r.Primero is { } g ? S(g, "NombreEmpresa") ?? "?" : "?");

        r = c.CompraAgilPublicadasAsync(new OpcionesCompraAgil { Regiones = new[] { 15 } }).GetAwaiter().GetResult();
        var codigos = r.Items.Select(i => S(i, "codigo")).ToList();
        Ver("Compra Ágil región 15 completa", r.Exito && !r.Truncado && r.Items.Count == r.Total && codigos.Count == codigos.Distinct().Count(), $"{r.Items.Count} de {r.Total}");
        var d = c.CompraAgilAsync(codigos.FirstOrDefault() ?? "").GetAwaiter().GetResult();
        var pa = d.Exito ? Participantes.De(d.Primero!.Value) : new QuienCompraQuienVende(Array.Empty<Parte>(), Array.Empty<Parte>());
        Ver("detalle de Compra Ágil: quién compra", d.Exito && pa.Compradores.Count > 0, pa.Compradores.FirstOrDefault()?.Nombre ?? "?");

        Console.WriteLine(fallas > 0 ? $"\n{fallas} con falla" : "\nTodo correcto contra la API real");
        return fallas > 0 ? 1 : 0;
    }
}
