use crate::{fecha, Opciones, Resultado};
use chrono::NaiveDate;
use serde_json::Value;
use std::collections::HashSet;
use std::sync::Mutex;
use std::thread::sleep;
use std::time::{Duration, Instant};

const REGIONES: u8 = 16;
const TAMANO_PAGINA: u32 = 50;
/// Tope del cuerpo de una respuesta (bytes). ureq 3 corta en 10 MB por defecto; el listado de licitaciones activas ya pesa ~0,8 MB.
const LIMITE_CUERPO: u64 = 64 * 1024 * 1024;

/// Filtros de licitaciones y órdenes de compra. Con `fecha` el listado trae solo código, nombre y estado;
/// el DETALLE (comprador, adjudicación, ítems) solo viene al consultar por `codigo`.
#[derive(Debug, Clone, Default)]
pub struct Filtros {
    /// Día calendario de Chile (ver [`crate::hoy`]).
    pub fecha: Option<NaiveDate>,
    /// Licitaciones: activas, publicada, cerrada, desierta, adjudicada, revocada, suspendida, todos.
    /// Órdenes: enviadaproveedor, aceptada, cancelada, recepcionconforme...
    pub estado: Option<String>,
    pub codigo: Option<String>,
    pub codigo_organismo: Option<String>,
    pub codigo_proveedor: Option<String>,
}

/// Opciones del listado de Compras Ágiles publicadas.
#[derive(Debug, Clone, Default)]
pub struct OpcionesCompraAgil {
    /// Filtro de texto de la API (`q`). Poco fiable a nivel nacional (responde 504): filtre localmente si puede.
    pub palabra_clave: Option<String>,
    /// Códigos de región 1 a 16. Vacío = recorrer las 16 y deduplicar.
    pub regiones: Vec<u8>,
    /// Tope del recorrido en segundos; al agotarse devuelve lo acumulado con `truncado = true`.
    pub max_segundos: Option<u64>,
}

/// Cliente de las APIs públicas de Mercado Público (ver la documentación del crate).
pub struct Cliente {
    o: Opciones,
    agente: ureq::Agent,
    ultima: Mutex<Option<Instant>>,
}

/// Verdadero si el código de una orden de compra es de Convenio Marco (sufijo `-CM<año>`).
pub fn es_convenio_marco(codigo_orden: &str) -> bool {
    let b = codigo_orden.as_bytes();
    b.len() >= 5 && &b[b.len() - 5..b.len() - 2] == b"-CM" && b[b.len() - 2].is_ascii_digit() && b[b.len() - 1].is_ascii_digit()
}

impl Cliente {
    pub fn nuevo(opciones: Opciones) -> Result<Self, String> {
        if opciones.max_intentos < 1 {
            return Err("max_intentos debe ser al menos 1.".into());
        }
        // http_status_as_error(false): un 4xx/5xx también trae cuerpo JSON con el mensaje oficial, que hay que leer.
        let agente: ureq::Agent = ureq::Agent::config_builder()
            .timeout_global(Some(Duration::from_secs(opciones.timeout_segundos)))
            .http_status_as_error(false)
            .build()
            .into();
        Ok(Self {
            o: opciones,
            agente,
            ultima: Mutex::new(None),
        })
    }

    // ───────────────────────── Licitaciones ─────────────────────────

    pub fn licitaciones(&self, filtros: &Filtros) -> Resultado {
        self.v1("licitaciones.json", Some(filtros), "Listado", &[])
    }

    /// Detalle completo de una licitación (ej. "1001-18-LE26"): comprador, ítems y adjudicación.
    pub fn licitacion(&self, codigo: &str) -> Resultado {
        self.licitaciones(&Filtros {
            codigo: Some(codigo.into()),
            ..Default::default()
        })
    }

    /// Todas las licitaciones activas hoy (unas 5.000).
    pub fn licitaciones_activas(&self) -> Resultado {
        self.licitaciones(&Filtros {
            estado: Some("activas".into()),
            ..Default::default()
        })
    }

    // ───────────── Órdenes de compra y Convenio Marco ─────────────

    pub fn ordenes_compra(&self, filtros: &Filtros) -> Resultado {
        self.v1("ordenesdecompra.json", Some(filtros), "Listado", &[])
    }

    /// Detalle completo de una orden de compra: comprador, proveedor, ítems y montos.
    pub fn orden_compra(&self, codigo: &str) -> Resultado {
        self.ordenes_compra(&Filtros {
            codigo: Some(codigo.into()),
            ..Default::default()
        })
    }

    /// Órdenes de Convenio Marco de un día (código con sufijo `-CM<año>`): ~2.000 de las ~18.000 del día.
    pub fn ordenes_convenio_marco(&self, fecha: NaiveDate, filtros: &Filtros) -> Resultado {
        let res = self.ordenes_compra(&Filtros {
            fecha: Some(fecha),
            ..filtros.clone()
        });
        if !res.exito {
            return res;
        }
        let cm: Vec<Value> = res
            .items
            .iter()
            .filter(|o| es_convenio_marco(o.get("Codigo").and_then(Value::as_str).unwrap_or("")))
            .cloned()
            .collect();
        let n = cm.len() as u64;
        Resultado::ok(cm, Some(n), false, res.mensaje, res.http)
    }

    // ───────────────── Quién compra / quién vende ─────────────────

    /// Los ~900 organismos compradores del Estado (`CodigoEmpresa`, `NombreEmpresa`).
    pub fn compradores(&self) -> Resultado {
        self.v1("Empresas/BuscarComprador", None, "listaEmpresas", &[])
    }

    /// Busca un proveedor por RUT (ej. "89.862.200-2"). Sin coincidencia: éxito con lista vacía.
    pub fn proveedores(&self, rut: &str) -> Resultado {
        self.v1("Empresas/BuscarProveedor", None, "listaEmpresas", &[("rutempresaproveedor", rut)])
    }

    // ─────────────────────── Compra Ágil (v2) ───────────────────────

    /// Detalle de una Compra Ágil (ej. "3754-146-COT26"): institución, productos, proveedores cotizando.
    pub fn compra_agil(&self, codigo: &str) -> Resultado {
        let (res, crudo) = self.ejecutar(&format!("{}/{}", self.o.url_base_v2, codificar(codigo)), true, "Listado");
        if !res.exito {
            return res;
        }
        let payload = crudo.and_then(|c| c.get("payload").filter(|p| p.is_object()).cloned());
        Resultado::ok(payload.into_iter().collect(), None, false, res.mensaje, res.http)
    }

    /// Compras Ágiles publicadas (recibiendo cotizaciones). Sin palabra clave ni regiones recorre las 16
    /// regiones y deduplica, porque una consulta nacional solo pagina hasta 10.000 resultados.
    pub fn compra_agil_publicadas(&self, opciones: &OpcionesCompraAgil) -> Resultado {
        let palabra = opciones.palabra_clave.as_deref().unwrap_or("").trim().to_string();
        let mut regiones: Vec<u8> = vec![];
        for r in &opciones.regiones {
            if !regiones.contains(r) {
                regiones.push(*r);
            }
        }
        let inicio = Instant::now();
        let limite = opciones.max_segundos.map(|s| s as f64);

        if !palabra.is_empty() || !regiones.is_empty() {
            return self.recorrer(&palabra, &regiones, inicio, limite);
        }

        let mut vistos: HashSet<String> = HashSet::new();
        let mut items: Vec<Value> = vec![];
        let mut truncado = false;
        let mut alguna = false;
        let mut ultimo_error: Option<Resultado> = None;
        for region in 1..=REGIONES {
            if limite.is_some_and(|l| inicio.elapsed().as_secs_f64() > l) {
                truncado = true;
                break;
            }
            let res = self.recorrer("", &[region], inicio, limite);
            if !res.exito {
                truncado = true;
                ultimo_error = Some(res);
                continue;
            }
            alguna = true;
            truncado |= res.truncado;
            for item in res.items {
                let clave = item
                    .get("codigo")
                    .and_then(Value::as_str)
                    .map(str::to_string)
                    .unwrap_or_else(|| format!("sin-codigo-{}", items.len()));
                if vistos.insert(clave) {
                    items.push(item);
                }
            }
        }
        if !alguna {
            return ultimo_error.unwrap_or_else(|| Resultado::error("Se agotó el tiempo antes de consultar la primera región.", None, None));
        }
        let n = items.len() as u64;
        let msg = if truncado {
            "Listado parcial: alguna región falló o se agotó el tiempo."
        } else {
            "Conexión exitosa."
        };
        Resultado::ok(items, Some(n), truncado, msg, Some(200))
    }

    // ─────────────────────────── Internos ───────────────────────────

    fn recorrer(&self, palabra: &str, regiones: &[u8], inicio: Instant, limite: Option<f64>) -> Resultado {
        let mut items: Vec<Value> = vec![];
        let mut total: Option<u64> = None;
        let mut pagina = 1u64;
        let mut paginas = 1u64;
        loop {
            if limite.is_some_and(|l| inicio.elapsed().as_secs_f64() > l) {
                return Resultado::ok(
                    items,
                    total,
                    true,
                    format!("Listado parcial: se agotó el tiempo en la página {pagina} de {paginas}."),
                    Some(200),
                );
            }
            let mut q: Vec<(&str, String)> = vec![
                ("estado", "publicada".into()),
                ("tamano_pagina", TAMANO_PAGINA.to_string()),
                ("numero_pagina", pagina.to_string()),
            ];
            if !palabra.is_empty() {
                q.push(("q", palabra.to_string()));
            }
            if !regiones.is_empty() {
                q.push(("region", regiones.iter().map(u8::to_string).collect::<Vec<_>>().join(",")));
            }
            let (res, crudo) = self.ejecutar(&format!("{}?{}", self.o.url_base_v2, query(&q)), true, "Listado");
            if !res.exito {
                if !items.is_empty() {
                    return Resultado::ok(
                        items,
                        total,
                        true,
                        format!("Listado parcial: falló la página {pagina} de {paginas} ({}).", res.mensaje),
                        Some(200),
                    );
                }
                return res;
            }
            items.extend(res.items);
            if let Some(pg) = crudo.as_ref().and_then(|c| c.get("payload")).and_then(|p| p.get("paginacion")) {
                paginas = pg.get("total_paginas").and_then(Value::as_u64).unwrap_or(paginas);
                total = pg.get("total_resultados").and_then(Value::as_u64).or(total);
            }
            pagina += 1;
            if pagina > paginas {
                return Resultado::ok(items, total, false, "Conexión exitosa.", Some(200));
            }
        }
    }

    fn v1(&self, ruta: &str, f: Option<&Filtros>, clave: &str, extra: &[(&str, &str)]) -> Resultado {
        let mut q: Vec<(&str, String)> = vec![("ticket", self.o.ticket.clone())];
        if let Some(f) = f {
            if let Some(fe) = f.fecha {
                q.push(("fecha", fecha::formato(fe)));
            }
            for (nombre, valor) in [
                ("estado", &f.estado),
                ("codigo", &f.codigo),
                ("CodigoOrganismo", &f.codigo_organismo),
                ("CodigoProveedor", &f.codigo_proveedor),
            ] {
                if let Some(v) = valor.as_ref().filter(|v| !v.trim().is_empty()) {
                    q.push((nombre, v.clone()));
                }
            }
            if q.len() == 1 {
                return Resultado::error("Indique al menos un filtro válido: fecha, estado o codigo.", None, None);
            }
        }
        for (k, v) in extra {
            q.push((k, (*v).to_string()));
        }
        let url = format!("{}/{}?{}", self.o.url_base_v1.trim_end_matches('/'), ruta, query(&q));
        self.ejecutar(&url, false, clave).0
    }

    /// Ejecuta con reintentos ante errores transitorios.
    fn ejecutar(&self, url: &str, v2: bool, clave: &str) -> (Resultado, Option<Value>) {
        let mut intento = 1;
        loop {
            let (http, cuerpo) = self.get(url, v2);
            let (res, crudo, transitorio) = if v2 {
                interpretar_v2(http, &cuerpo)
            } else {
                interpretar_v1(http, &cuerpo, clave)
            };
            if !transitorio || intento >= self.o.max_intentos {
                return (res, crudo);
            }
            sleep(Duration::from_millis(u64::from(intento) * self.o.espera_reintento_ms));
            intento += 1;
        }
    }

    fn get(&self, url: &str, v2: bool) -> (u16, String) {
        let espera = {
            let ultima = self.ultima.lock().unwrap();
            ultima.map(|t| Duration::from_millis(self.o.intervalo_minimo_ms).saturating_sub(t.elapsed()))
        };
        if let Some(e) = espera.filter(|e| !e.is_zero()) {
            sleep(e);
        }
        let mut req = self
            .agente
            .get(url)
            .header("User-Agent", &self.o.user_agent)
            .header("Accept", "application/json");
        if v2 {
            req = req.header("ticket", &self.o.ticket);
        }
        let salida = match req.call() {
            // Sin subir el tope por defecto de ureq 3 (10 MB), un listado grande se cortaría y parecería un fallo de red.
            Ok(mut r) => (
                r.status().as_u16(),
                r.body_mut().with_config().limit(LIMITE_CUERPO).read_to_string().unwrap_or_default(),
            ),
            Err(_) => (0, String::new()),
        };
        *self.ultima.lock().unwrap() = Some(Instant::now());
        salida
    }
}

fn interpretar_v1(http: u16, cuerpo: &str, clave: &str) -> (Resultado, Option<Value>, bool) {
    let http_opt = (http != 0).then_some(http);
    if cuerpo.is_empty() {
        return (
            Resultado::error("No fue posible contactar a Mercado Público. Verifique la conectividad.", None, http_opt),
            None,
            true,
        );
    }
    let datos: Value = match serde_json::from_str(cuerpo) {
        Ok(v @ Value::Object(_)) => v,
        _ => {
            return (
                Resultado::error(format!("Respuesta inesperada de Mercado Público (HTTP {http})."), None, http_opt),
                None,
                http == 429 || http >= 500,
            )
        }
    };
    if let Some(lista) = datos.get(clave) {
        let items = lista.as_array().cloned().unwrap_or_default();
        let total = datos.get("Cantidad").and_then(Value::as_u64);
        return (Resultado::ok(items, total, false, "Conexión exitosa.", http_opt), Some(datos), false);
    }
    let codigo = datos.get("Codigo").and_then(Value::as_i64);
    let mensaje = datos
        .get("Mensaje")
        .and_then(Value::as_str)
        .unwrap_or("Mercado Público rechazó la solicitud.")
        .to_string();
    // 10200 = "No hay resultados": la consulta fue válida, simplemente no hay datos.
    if codigo == Some(10200) {
        return (Resultado::ok(vec![], Some(0), false, mensaje, http_opt), Some(datos), false);
    }
    let transitorio = codigo == Some(10500) || http == 429 || mensaje.to_lowercase().contains("simult");
    (Resultado::error(mensaje, codigo, http_opt), Some(datos), transitorio)
}

fn interpretar_v2(http: u16, cuerpo: &str) -> (Resultado, Option<Value>, bool) {
    let http_opt = (http != 0).then_some(http);
    if cuerpo.is_empty() {
        return (Resultado::error("No fue posible contactar la API de Compra Ágil.", None, http_opt), None, true);
    }
    let transitorio = http == 408 || http == 429 || http >= 500;
    let datos: Value = match serde_json::from_str(cuerpo) {
        Ok(v @ Value::Object(_)) => v,
        _ => {
            return (
                Resultado::error(format!("Respuesta inesperada de la API de Compra Ágil (HTTP {http})."), None, http_opt),
                None,
                transitorio,
            )
        }
    };
    if datos.get("success").and_then(Value::as_str) == Some("OK") {
        let items = datos
            .get("payload")
            .and_then(|p| p.get("items"))
            .and_then(Value::as_array)
            .cloned()
            .unwrap_or_default();
        return (Resultado::ok(items, None, false, "Conexión exitosa.", http_opt), Some(datos), false);
    }
    let mensaje = datos
        .get("errors")
        .and_then(|e| e.get(0))
        .and_then(|e| e.get("mensaje"))
        .and_then(Value::as_str)
        .or_else(|| datos.get("message").and_then(Value::as_str))
        .unwrap_or("La API de Compra Ágil rechazó la solicitud.")
        .to_string();
    (Resultado::error(mensaje, http_opt.map(i64::from), http_opt), Some(datos), transitorio)
}

/// Codificación RFC 3986 (espacio = `%20`): la API v2 responde 403 si un parámetro trae `+`.
fn codificar(texto: &str) -> String {
    let mut s = String::new();
    for b in texto.bytes() {
        if b.is_ascii_alphanumeric() || matches!(b, b'-' | b'.' | b'_' | b'~') {
            s.push(b as char);
        } else {
            s.push_str(&format!("%{b:02X}"));
        }
    }
    s
}

fn query(q: &[(&str, String)]) -> String {
    q.iter()
        .map(|(k, v)| format!("{}={}", codificar(k), codificar(v)))
        .collect::<Vec<_>>()
        .join("&")
}
