# mercado_publico (Rust)

Cliente de la API pública de **Mercado Público (ChileCompra)**: licitaciones, órdenes de compra, convenios marco, Compra Ágil y **quién compra / quién vende**. Rust 1.88+. Licencia MIT.

```toml
[dependencies]
mercado_publico = "1"
```

```rust
use mercado_publico::*;

// El ticket se pide gratis en api.mercadopublico.cl; léalo de una variable de entorno, nunca del código.
let c = Cliente::nuevo(Opciones::nuevo(std::env::var("MERCADOPUBLICO_TICKET")?)?)?;

let r = c.licitacion("2107-152-LE26");                 // detalle de una licitación
let p = de(r.primero().unwrap());                        // quién compra y quién vende
println!("{:?} ← {:?}", p.compradores[0].nombre, p.vendedores[0].nombre);

let cm = c.ordenes_convenio_marco(hoy().pred_opt().unwrap(), &Filtros::default());
let ca = c.compra_agil_publicadas(&OpcionesCompraAgil { regiones: vec![15], ..Default::default() });
if ca.truncado { /* el listado es parcial: faltan registros */ }
```

Las fallas de la API **no son `Err`**: se devuelven en `Resultado` con `exito = false` y el mensaje oficial. Reintenta solo los errores transitorios (peticiones simultáneas, 429, 5xx, red) y espacia las consultas a 1 por segundo. El cliente es síncrono (`ureq`).

**Alcance, límites y lo comprobado contra la API real:** <https://github.com/saraivaruas/mercado-publico-api#readme>. Proyecto independiente, no oficial.
