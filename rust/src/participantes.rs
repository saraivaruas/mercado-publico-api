use serde_json::Value;
use std::collections::HashSet;

/// Una parte de una compra: quien compra (organismo) o quien vende (proveedor).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Parte {
    /// `"comprador"` o `"vendedor"`.
    pub rol: &'static str,
    pub nombre: Option<String>,
    pub rut: Option<String>,
    /// Código interno de Mercado Público del organismo o proveedor, si viene.
    pub codigo: Option<String>,
    /// Unidad de compra (comprador) o sucursal (vendedor).
    pub unidad: Option<String>,
    pub region: Option<String>,
    pub comuna: Option<String>,
    /// `"licitacion"`, `"orden_compra"` o `"compra_agil"`.
    pub fuente: &'static str,
}

/// Compradores y vendedores de una compra.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct QuienCompraQuienVende {
    pub compradores: Vec<Parte>,
    pub vendedores: Vec<Parte>,
}

const COMPRADOR: &str = "comprador";
const VENDEDOR: &str = "vendedor";

/// Extrae "quién compra y quién vende" del DETALLE de una licitación, orden de compra (incluye
/// Convenio Marco) o Compra Ágil, detectando el tipo por su forma. Solo el detalle trae estos datos:
/// los listados por fecha traen únicamente código, nombre y estado. Una licitación solo informa
/// vendedores cuando ya está adjudicada; una Compra Ágil, los proveedores que cotizaron.
pub fn de(detalle: &Value) -> QuienCompraQuienVende {
    if detalle.get("CodigoExterno").is_some() {
        de_licitacion(detalle)
    } else if detalle.get("institucion").is_some() {
        de_compra_agil(detalle)
    } else if detalle.get("Comprador").is_some() || detalle.get("Proveedor").is_some() {
        de_orden_compra(detalle)
    } else {
        QuienCompraQuienVende::default()
    }
}

pub fn de_licitacion(d: &Value) -> QuienCompraQuienVende {
    let compradores = obj(d, "Comprador")
        .map(|c| {
            vec![Parte {
                rol: COMPRADOR,
                nombre: txt(c, "NombreOrganismo"),
                rut: txt(c, "RutUnidad"),
                codigo: txt(c, "CodigoOrganismo"),
                unidad: txt(c, "NombreUnidad"),
                region: txt(c, "RegionUnidad"),
                comuna: txt(c, "ComunaUnidad"),
                fuente: "licitacion",
            }]
        })
        .unwrap_or_default();

    let mut vistos = HashSet::new();
    let mut vendedores = vec![];
    if let Some(items) = d.get("Items").and_then(|i| i.get("Listado")).and_then(Value::as_array) {
        for item in items {
            let Some(a) = obj(item, "Adjudicacion") else { continue };
            let Some(rut) = txt(a, "RutProveedor") else { continue };
            if !vistos.insert(rut.clone()) {
                continue;
            }
            vendedores.push(Parte {
                rol: VENDEDOR,
                nombre: txt(a, "NombreProveedor"),
                rut: Some(rut),
                codigo: None,
                unidad: None,
                region: None,
                comuna: None,
                fuente: "licitacion",
            });
        }
    }
    QuienCompraQuienVende { compradores, vendedores }
}

pub fn de_orden_compra(d: &Value) -> QuienCompraQuienVende {
    let compradores = obj(d, "Comprador")
        .map(|c| {
            vec![Parte {
                rol: COMPRADOR,
                nombre: txt(c, "NombreOrganismo"),
                rut: txt(c, "RutUnidad"),
                codigo: txt(c, "CodigoOrganismo"),
                unidad: txt(c, "NombreUnidad"),
                region: txt(c, "RegionUnidad"),
                comuna: txt(c, "ComunaUnidad"),
                fuente: "orden_compra",
            }]
        })
        .unwrap_or_default();
    let vendedores = obj(d, "Proveedor")
        .map(|p| {
            vec![Parte {
                rol: VENDEDOR,
                nombre: txt(p, "Nombre"),
                rut: txt(p, "RutSucursal"),
                codigo: txt(p, "Codigo"),
                unidad: txt(p, "NombreSucursal"),
                region: txt(p, "Region"),
                comuna: txt(p, "Comuna"),
                fuente: "orden_compra",
            }]
        })
        .unwrap_or_default();
    QuienCompraQuienVende { compradores, vendedores }
}

pub fn de_compra_agil(d: &Value) -> QuienCompraQuienVende {
    let compradores = obj(d, "institucion")
        .map(|i| {
            vec![Parte {
                rol: COMPRADOR,
                nombre: txt(i, "organismo_comprador"),
                rut: txt(i, "rut"),
                codigo: None,
                unidad: txt(i, "unidad_compra"),
                region: txt(i, "nombre_region"),
                comuna: None,
                fuente: "compra_agil",
            }]
        })
        .unwrap_or_default();

    let mut vendedores = vec![];
    if let Some(lista) = d.get("proveedores_cotizando").and_then(Value::as_array) {
        for p in lista.iter().filter(|p| p.is_object()) {
            let nombre = txt(p, "razon_social").or_else(|| txt(p, "nombre")).or_else(|| txt(p, "nombre_proveedor"));
            let rut = txt(p, "rut").or_else(|| txt(p, "rut_proveedor"));
            if nombre.is_none() && rut.is_none() {
                continue;
            }
            vendedores.push(Parte {
                rol: VENDEDOR,
                nombre,
                rut,
                codigo: None,
                unidad: None,
                region: None,
                comuna: None,
                fuente: "compra_agil",
            });
        }
    }
    QuienCompraQuienVende { compradores, vendedores }
}

fn obj<'a>(v: &'a Value, nombre: &str) -> Option<&'a Value> {
    v.get(nombre).filter(|x| x.is_object())
}

fn txt(v: &Value, nombre: &str) -> Option<String> {
    let t = match v.get(nombre)? {
        Value::String(s) => s.trim().to_string(),
        Value::Number(n) => n.to_string(),
        _ => return None,
    };
    if t.is_empty() {
        None
    } else {
        Some(t)
    }
}
