# Cambios

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
