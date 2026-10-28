# Lo que se sabe de la API de Mercado Público

Todo lo de aquí se **comprobó contra la API real en octubre de 2026** salvo que diga lo contrario. La API puede cambiar sin aviso.

## Endpoints

| Qué | URL |
|---|---|
| Licitaciones | `GET https://api.mercadopublico.cl/servicios/v1/publico/licitaciones.json?ticket=…` |
| Órdenes de compra (incluye Convenio Marco) | `…/ordenesdecompra.json?ticket=…` |
| Organismos compradores | `…/Empresas/BuscarComprador?ticket=…` → `listaEmpresas` (≈899) |
| Proveedor por RUT | `…/Empresas/BuscarProveedor?ticket=…&rutempresaproveedor=89.862.200-2` |
| Compra Ágil (listado) | `GET https://api2.mercadopublico.cl/v2/compra-agil?estado=publicada&tamano_pagina=50&numero_pagina=1` con la cabecera `ticket` |
| Compra Ágil (detalle) | `…/v2/compra-agil/{codigo}` con la cabecera `ticket` |

La API v1 recibe el ticket en la URL; la v2, en la **cabecera** `ticket`. El mismo ticket sirve para las dos.

## Filtros (v1)

`fecha` (ddmmaaaa), `estado`, `codigo`, `CodigoOrganismo`, `CodigoProveedor`.

- `estado=activas` en licitaciones, sin fecha: ≈4.945 (probado). `estado=adjudicada` con `fecha`: probado.
- `estado=aceptada` en órdenes de compra: probado (≈7.976 en un día). Otros estados: documentados por ChileCompra, no probados.
- `CodigoOrganismo` y `CodigoProveedor` funcionan en licitaciones y órdenes de compra (probado con un organismo y un proveedor).
- Un listado por fecha trae solo `CodigoExterno`/`Codigo`, `Nombre`, `CodigoEstado` (y `FechaCierre` en licitaciones). Un día: ≈1.300 licitaciones y
  ≈18.000 órdenes, de las cuales ≈2.040 son de Convenio Marco (sufijo `-CM<año>`).

## Quién compra / quién vende (solo en el detalle)

| Detalle | Comprador | Vendedor |
|---|---|---|
| Licitación | `Comprador` (`NombreOrganismo`, `RutUnidad`, `CodigoOrganismo`, `RegionUnidad`…) | `Items.Listado[].Adjudicacion` (`RutProveedor`, `NombreProveedor`), **solo si está adjudicada** |
| Orden de compra | `Comprador` | `Proveedor` (`Nombre`, `RutSucursal`, `Codigo`, `NombreSucursal`) |
| Compra Ágil | `institucion` (`organismo_comprador`, `rut`, `unidad_compra`, `nombre_region`) | `proveedores_cotizando[]` (vacío si nadie cotizó; la forma de cada elemento no se pudo verificar) |

## Errores y límites

| Situación | Respuesta | Qué hace la biblioteca |
|---|---|---|
| Ticket inválido (v1) | HTTP 203, `{"Codigo":203,"Mensaje":"Ticket no válido."}` | Error, no reintenta |
| Fecha futura (v1) | HTTP 500, `Codigo` 10300, «La fecha es mayor a la actual.» | Error, no reintenta. Use el día de Chile |
| Muchas consultas seguidas (v1) | `Codigo` 10500 «peticiones simultáneas», o **HTTP 429** | Reintenta con espera creciente |
| Proveedor sin resultados | HTTP 500, `Codigo` 10200 «No hay resultados de empresas.» | Éxito con lista vacía |
| Ticket inválido (v2) | HTTP 401, `errors[0].mensaje` | Error, no reintenta |
| Código inexistente (v2) | HTTP 400, `PARAMETRO_RUTA_INVALIDO` | Error, no reintenta |
| Sin `User-Agent` (v2) | HTTP 403 (WAF) | Siempre se envía uno |
| Parámetro `q` a nivel nacional (v2) | **HTTP 504** tras ~30 s, con cualquier palabra y tamaño de página | Disponible, pero sin garantía; filtre localmente |
| `tamano_pagina` > 50 (v2) | HTTP 400 | Se usa 50 |

- **Ritmo:** ≈1 consulta por segundo. Una consulta grande seguida de otra inmediata devolvió HTTP 429.
- **Espacios en parámetros (v2):** deben ir como `%20`; con `+` la API respondió 403.
- **Tope de 10.000 (v2):** `paginacion.total_resultados` nunca pasa de 10.000, pero la suma de las 16 regiones fue 10.292. El listado nacional
  deja fuera lo que excede el tope, por eso se recorre por región con `region=1..16` y se deduplica por `codigo`. Los registros sin región asignada, si
  existieran, no se podrían ver por este camino (no se pudo comprobar).
- Un listado de Compra Ágil sin filtro responde en ≈2,5 s por página de 50; con `region` y `q` juntos, una consulta tardó ≈28 s.
