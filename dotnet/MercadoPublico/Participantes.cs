using System.Text.Json;

namespace MercadoPublico;

/// <summary>Una parte de una compra: quien compra (organismo) o quien vende (proveedor).</summary>
public sealed record Parte(
    string Rol, string? Nombre, string? Rut, string? Codigo, string? Unidad, string? Region, string? Comuna, string Fuente)
{
    public const string Comprador = "comprador";
    public const string Vendedor = "vendedor";
}

/// <summary>Compradores y vendedores de una compra.</summary>
public sealed record QuienCompraQuienVende(IReadOnlyList<Parte> Compradores, IReadOnlyList<Parte> Vendedores);

/// <summary>
/// Extrae "quién compra y quién vende" del DETALLE de una licitación, orden de compra (incluye Convenio Marco)
/// o Compra Ágil. Solo el detalle trae estos datos: los listados por fecha traen únicamente código, nombre y estado.
/// Una licitación solo informa vendedores cuando ya está adjudicada; una Compra Ágil, los proveedores que cotizaron.
/// </summary>
public static class Participantes
{
    /// <summary>Detecta el tipo de detalle por su forma.</summary>
    public static QuienCompraQuienVende De(JsonElement detalle)
    {
        if (detalle.ValueKind != JsonValueKind.Object) return Vacio();
        if (detalle.TryGetProperty("CodigoExterno", out _)) return DeLicitacion(detalle);
        if (detalle.TryGetProperty("institucion", out _)) return DeCompraAgil(detalle);
        if (detalle.TryGetProperty("Comprador", out _) || detalle.TryGetProperty("Proveedor", out _)) return DeOrdenCompra(detalle);
        return Vacio();
    }

    public static QuienCompraQuienVende DeLicitacion(JsonElement d)
    {
        var compradores = new List<Parte>();
        if (Obj(d, "Comprador") is { } c)
            compradores.Add(new Parte(Parte.Comprador, Txt(c, "NombreOrganismo"), Txt(c, "RutUnidad"), Txt(c, "CodigoOrganismo"), Txt(c, "NombreUnidad"), Txt(c, "RegionUnidad"), Txt(c, "ComunaUnidad"), "licitacion"));

        var vendedores = new Dictionary<string, Parte>();
        if (Obj(d, "Items") is { } items && items.TryGetProperty("Listado", out var lista) && lista.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in lista.EnumerateArray())
            {
                if (Obj(item, "Adjudicacion") is not { } a || Txt(a, "RutProveedor") is not { } rut || vendedores.ContainsKey(rut)) continue;
                vendedores[rut] = new Parte(Parte.Vendedor, Txt(a, "NombreProveedor"), rut, null, null, null, null, "licitacion");
            }
        }
        return new QuienCompraQuienVende(compradores, vendedores.Values.ToList());
    }

    public static QuienCompraQuienVende DeOrdenCompra(JsonElement d)
    {
        var compradores = new List<Parte>();
        var vendedores = new List<Parte>();
        if (Obj(d, "Comprador") is { } c)
            compradores.Add(new Parte(Parte.Comprador, Txt(c, "NombreOrganismo"), Txt(c, "RutUnidad"), Txt(c, "CodigoOrganismo"), Txt(c, "NombreUnidad"), Txt(c, "RegionUnidad"), Txt(c, "ComunaUnidad"), "orden_compra"));
        if (Obj(d, "Proveedor") is { } p)
            vendedores.Add(new Parte(Parte.Vendedor, Txt(p, "Nombre"), Txt(p, "RutSucursal"), Txt(p, "Codigo"), Txt(p, "NombreSucursal"), Txt(p, "Region"), Txt(p, "Comuna"), "orden_compra"));
        return new QuienCompraQuienVende(compradores, vendedores);
    }

    public static QuienCompraQuienVende DeCompraAgil(JsonElement d)
    {
        var compradores = new List<Parte>();
        var vendedores = new List<Parte>();
        if (Obj(d, "institucion") is { } i)
            compradores.Add(new Parte(Parte.Comprador, Txt(i, "organismo_comprador"), Txt(i, "rut"), null, Txt(i, "unidad_compra"), Txt(i, "nombre_region"), null, "compra_agil"));
        if (d.TryGetProperty("proveedores_cotizando", out var lista) && lista.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in lista.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object) continue;
                var nombre = Txt(p, "razon_social") ?? Txt(p, "nombre") ?? Txt(p, "nombre_proveedor");
                var rut = Txt(p, "rut") ?? Txt(p, "rut_proveedor");
                if (nombre is null && rut is null) continue;
                vendedores.Add(new Parte(Parte.Vendedor, nombre, rut, null, null, null, null, "compra_agil"));
            }
        }
        return new QuienCompraQuienVende(compradores, vendedores);
    }

    private static QuienCompraQuienVende Vacio() => new(Array.Empty<Parte>(), Array.Empty<Parte>());

    private static JsonElement? Obj(JsonElement e, string nombre)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    private static string? Txt(JsonElement e, string nombre)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(nombre, out var v)) return null;
        var t = v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null };
        t = t?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }
}
