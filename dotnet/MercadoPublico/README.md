# MercadoPublico (.NET)

Cliente de la API pública de **Mercado Público (ChileCompra)**: licitaciones, órdenes de compra, convenios marco, Compra Ágil y **quién compra / quién vende**. Sin dependencias. .NET 8 y 10. Licencia MIT.

```bash
dotnet add package MercadoPublico
```

```csharp
using MercadoPublico;

// El ticket se pide gratis en api.mercadopublico.cl; léalo de una variable de entorno, nunca del código.
using var c = new Cliente(new Opciones(Environment.GetEnvironmentVariable("MERCADOPUBLICO_TICKET")!));

var r = await c.LicitacionAsync("2107-152-LE26");           // detalle de una licitación
var p = Participantes.De(r.Primero!.Value);                  // quién compra y quién vende
Console.WriteLine($"{p.Compradores[0].Nombre} ← {p.Vendedores[0].Nombre}");

var cm = await c.OrdenesConvenioMarcoAsync(FechaChile.Hoy().AddDays(-1));
var ca = await c.CompraAgilPublicadasAsync(new OpcionesCompraAgil { Regiones = new[] { 15 } });
if (ca.Truncado) { /* el listado es parcial: faltan registros */ }
```

Las fallas de la API **no lanzan excepciones**: se devuelven con `Exito = false` y el mensaje oficial. Reintenta solo los errores transitorios (peticiones simultáneas, 429, 5xx, red) y espacia las consultas a 1 por segundo.

**Alcance, límites y lo comprobado contra la API real:** <https://github.com/saraivaruas/mercado-publico-api#readme>. Proyecto independiente, no oficial.
