# mercado-publico-api

**Cliente de la API pública de Mercado Público (ChileCompra)**, en **PHP**, **C#/.NET** y **Rust**, con el mismo comportamiento en los tres. Trae
**licitaciones, órdenes de compra, convenios marco y Compra Ágil**, y permite responder **quién compra y quién vende**. Software libre, licencia **MIT**.
Sin dependencias externas en PHP y C#.

> **Mercado Público y ChileCompra son lo mismo para esta biblioteca.** ChileCompra es la Dirección de Compras Públicas, el organismo del Estado;
> Mercado Público es su plataforma de compras. Aquí se usan sus dos APIs oficiales con un mismo ticket: `api.mercadopublico.cl` (licitaciones,
> órdenes de compra, empresas) y `api2.mercadopublico.cl` (Compra Ágil).

> ## Alcance real — léalo antes de usarla
>
> - **Es un proyecto independiente y no oficial.** No está afiliado a ChileCompra ni a la Dirección de Compras Públicas. Usted debe pedir su propio ticket
>   gratuito en [api.mercadopublico.cl](https://api.mercadopublico.cl) y respetar sus condiciones de uso.
> - **Verificado contra la API real en octubre de 2026** (las tres versiones, con resultados idénticos): licitaciones activas, detalle de una licitación
>   adjudicada, órdenes de Convenio Marco de un día, detalle de una orden, compradores, proveedor por RUT, Compra Ágil completa de una región y su detalle.
>   Los estados de licitación distintos de `activas` y `adjudicada` están documentados por ChileCompra pero **no los probé**.
> - **Los listados solo traen código, nombre y estado.** Quién compra y quién vende está únicamente en el **detalle**, que se consulta de a uno a ~1 por
>   segundo. Un día tiene ~18.000 órdenes de compra: obtener el detalle de todas toma horas. Elija qué detallar.
> - **Una licitación solo informa vendedores cuando ya está adjudicada**; una Compra Ágil, solo los proveedores que han cotizado.
> - **Convenio Marco** se entrega como lo que es en la API: las **órdenes de compra** de Convenio Marco (código con sufijo `-CM<año>`). No incluye el
>   catálogo de productos ni precios del convenio, que la API pública no expone.
> - **Compra Ágil**: la API solo deja paginar 10.000 resultados por consulta y hay más publicadas (la suma por región fue ~10.300), por eso se recorre
>   región por región. Una región tarda ~4 s por página de 50; el país completo, unos 16 minutos. Un listado parcial se marca `truncado`.
> - Se entrega «tal cual», sin garantía (ver [LICENSE](LICENSE)). La API puede cambiar sin aviso; por eso los elementos se entregan sin transformar.

## Qué hace

| Función | PHP | C# | Rust |
|---|:-:|:-:|:-:|
| Licitaciones: por fecha, estado, organismo o proveedor; activas; detalle por código | ✔ | ✔ | ✔ |
| Órdenes de compra: por fecha, estado, organismo o proveedor; detalle por código | ✔ | ✔ | ✔ |
| Convenio Marco: órdenes `-CM<año>` de un día | ✔ | ✔ | ✔ |
| Compra Ágil: publicadas (16 regiones, sin duplicados) y detalle | ✔ | ✔ | ✔ |
| Compradores (≈900 organismos) y proveedor por RUT | ✔ | ✔ | ✔ |
| **Quién compra / quién vende** desde el detalle de licitación, orden o Compra Ágil | ✔ | ✔ | ✔ |
| Reintento ante «peticiones simultáneas» (10500), HTTP 429, 5xx y red; 1 consulta por segundo | ✔ | ✔ | ✔ |
| Día calendario de Chile (`America/Santiago`) y fecha `ddmmaaaa` | ✔ | ✔ | ✔ |
| Listados parciales marcados como `truncado`, nunca éxito silencioso | ✔ | ✔ | ✔ |

Las fallas de la API **no lanzan excepciones**: se devuelven con `exito = false` y el mensaje oficial (por ejemplo `Ticket no válido.`).

## Uso

El ticket se lee de una variable de entorno; **nunca lo escriba en el código**.

### PHP (8.1+)

```php
use MercadoPublico\{Cliente, Opciones, Participantes};

$c = new Cliente(new Opciones(getenv('MERCADOPUBLICO_TICKET')));

$r = $c->licitacion('2107-152-LE26');          // detalle
$p = Participantes::de($r->primero());          // quién compra y quién vende
echo $p['compradores'][0]->nombre, ' ← ', $p['vendedores'][0]->nombre;

$cm = $c->ordenesConvenioMarco(new DateTimeImmutable('-1 day'));
$ca = $c->compraAgilPublicadas(['regiones' => [15]]);   // ['maxSegundos' => 600] para acotar el tiempo
if ($ca->truncado) { /* faltan registros: reintente luego */ }
```

### C# (.NET 8 y 10)

```csharp
using MercadoPublico;

using var c = new Cliente(new Opciones(Environment.GetEnvironmentVariable("MERCADOPUBLICO_TICKET")!));

var r = await c.LicitacionAsync("2107-152-LE26");
var p = Participantes.De(r.Primero!.Value);
Console.WriteLine($"{p.Compradores[0].Nombre} ← {p.Vendedores[0].Nombre}");

var cm = await c.OrdenesConvenioMarcoAsync(FechaChile.Hoy().AddDays(-1));
var ca = await c.CompraAgilPublicadasAsync(new OpcionesCompraAgil { Regiones = new[] { 15 } });
if (ca.Truncado) { /* faltan registros */ }
```

### Rust (1.88+)

```rust
use mercado_publico::*;

let c = Cliente::nuevo(Opciones::nuevo(std::env::var("MERCADOPUBLICO_TICKET")?)?)?;

let r = c.licitacion("2107-152-LE26");
let p = de(r.primero().unwrap());
println!("{:?} ← {:?}", p.compradores[0].nombre, p.vendedores[0].nombre);

let cm = c.ordenes_convenio_marco(hoy().pred_opt().unwrap(), &Filtros::default());
let ca = c.compra_agil_publicadas(&OpcionesCompraAgil { regiones: vec![15], ..Default::default() });
if ca.truncado { /* faltan registros */ }
```

Lo aprendido de la API real (límites, códigos de error, formas de respuesta) está en [docs/API.md](docs/API.md).

## Pruebas

Las pruebas normales corren contra un servidor local de mentira y **no necesitan ticket**: reintentos, 429, ticket inválido, deduplicación de regiones,
`%20` en los parámetros, truncado por página caída o por tiempo, y la extracción de compradores y vendedores. Hay 18 pruebas por lenguaje.

```bash
php php/tests/run.php
dotnet run --project dotnet/MercadoPublico.Tests -f net8.0      # y net10.0
(cd rust && cargo test)
```

Las pruebas **vivas** consultan la API real (solo lectura) con su propio ticket; ver [CONTRIBUTING.md](CONTRIBUTING.md).

## Contribuir

Solo se aceptan **pull requests**. Lea [CONTRIBUTING.md](CONTRIBUTING.md). Vulnerabilidades: [SECURITY.md](SECURITY.md).

## Licencia

[MIT](LICENSE) © 2026 Jorge Luis Saraiva Ruas
