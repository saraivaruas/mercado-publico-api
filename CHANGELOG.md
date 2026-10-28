# Cambios

## 1.0.1 — 2026-10

- **Rust:** migra `ureq` de la versión 2 a la 3, con la misma API pública. Se mantiene Rust 1.88 como versión mínima.
  Verificado con las 18 pruebas offline y con la prueba viva contra la API real.
- **PHP y C#:** sin cambios de comportamiento. La versión sube igual porque los tres paquetes comparten número.
- El CI ahora ejercita el empaquetado (`dotnet pack`, subida y descarga de artefactos, `cargo package`, `composer validate`) y
  las acciones de GitHub se actualizaron (`checkout` 7, `setup-dotnet` 6, `upload-artifact` 7, `download-artifact` 8).

## 1.0.0 — 2026-10

Primera versión pública, en PHP, C# y Rust con el mismo comportamiento.

- Licitaciones (por fecha, estado, organismo, proveedor y detalle por código), órdenes de compra, Convenio Marco,
  Compra Ágil, compradores y proveedores.
- «Quién compra y quién vende»: extracción de compradores y vendedores del detalle de licitaciones, órdenes de compra y
  Compra Ágil.
- Reintentos ante «peticiones simultáneas» (código 10500), HTTP 429, 5xx y fallas de red; separación mínima de 1 s entre
  consultas.
- Compra Ágil recorrida por región (la API corta a 10.000 resultados por consulta) y deduplicada; listados parciales
  marcados como `truncado`.
