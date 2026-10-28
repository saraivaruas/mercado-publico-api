//! Pruebas contra un servidor de mentira local que imita las APIs de Mercado Público.
//! El "ticket" elige el escenario.

use chrono::NaiveDate;
use mercado_publico::*;
use serde_json::{json, Value};
use std::collections::HashMap;
use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream};
use std::sync::{Mutex, OnceLock};
use std::thread;
use std::time::Duration;

static CONTADORES: OnceLock<Mutex<HashMap<String, u32>>> = OnceLock::new();
static PUERTO: OnceLock<u16> = OnceLock::new();

fn contar(clave: &str) -> u32 {
    let mut m = CONTADORES.get_or_init(Default::default).lock().unwrap();
    let n = m.entry(clave.to_string()).or_insert(0);
    let antes = *n;
    *n += 1;
    antes
}

fn decodificar(s: &str) -> String {
    let b = s.as_bytes();
    let mut out = vec![];
    let mut i = 0;
    while i < b.len() {
        if b[i] == b'%' && i + 2 < b.len() {
            out.push(u8::from_str_radix(&s[i + 1..i + 3], 16).unwrap_or(b'?'));
            i += 3;
        } else {
            out.push(if b[i] == b'+' { b' ' } else { b[i] });
            i += 1;
        }
    }
    String::from_utf8_lossy(&out).into_owned()
}

fn responder(mut s: TcpStream, http: u16, cuerpo: &str) {
    let r = format!(
        "HTTP/1.1 {http} X\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{cuerpo}",
        cuerpo.len()
    );
    let _ = s.write_all(r.as_bytes());
}

fn atender(mut s: TcpStream) {
    let mut buf = vec![0u8; 8192];
    let n = s.read(&mut buf).unwrap_or(0);
    let texto = String::from_utf8_lossy(&buf[..n]).into_owned();
    let mut lineas = texto.split("\r\n");
    let primera = lineas.next().unwrap_or("");
    let objetivo = primera.split(' ').nth(1).unwrap_or("/");
    let cab: HashMap<String, String> = lineas
        .take_while(|l| !l.is_empty())
        .filter_map(|l| l.split_once(": "))
        .map(|(k, v)| (k.to_lowercase(), v.to_string()))
        .collect();
    let (path, query_raw) = objetivo.split_once('?').unwrap_or((objetivo, ""));
    let q: HashMap<String, String> = query_raw
        .split('&')
        .filter_map(|p| p.split_once('='))
        .map(|(k, v)| (decodificar(k), decodificar(v)))
        .collect();

    if path.starts_with("/v2") {
        let ticket = cab.get("ticket").cloned().unwrap_or_default();
        if ticket.is_empty() || cab.get("user-agent").map_or(true, |u| u.is_empty()) {
            return responder(s, 403, r#"{"message":"Forbidden"}"#);
        }
        if ticket.starts_with("lento") {
            thread::sleep(Duration::from_millis(150));
        }
        if let Some(codigo) = path.strip_prefix("/v2/compra-agil/") {
            if codigo == "MALO" {
                return responder(
                    s,
                    400,
                    r#"{"success":"NOK","payload":null,"errors":[{"codigo":"X","mensaje":"Parámetro de ruta 'codigo' inválido."}]}"#,
                );
            }
            let cuerpo = json!({"success":"OK","payload":{"codigo":codigo,"institucion":{"organismo_comprador":"MUNI X","rut":"69.041.300-0","unidad_compra":"SALUD","nombre_region":"Región de Coquimbo"},"proveedores_cotizando":[{"razon_social":"PROV UNO","rut":"1-9"}]}});
            return responder(s, 200, &cuerpo.to_string());
        }
        let region: u32 = q.get("region").and_then(|r| r.parse().ok()).unwrap_or(0);
        let pagina: u32 = q.get("numero_pagina").and_then(|r| r.parse().ok()).unwrap_or(1);
        if ticket.starts_with("fallapag") && region == 3 && pagina == 2 {
            return responder(s, 500, r#"{"message":"Internal"}"#);
        }
        let mut items = vec![json!({"codigo": format!("R{region}-P{pagina}-A"), "q": q.get("q"), "raw": query_raw, "nombre": "x"})];
        if pagina == 1 && (region == 1 || region == 2) {
            items.push(json!({"codigo":"REPETIDA","nombre":"y"}));
        }
        let cuerpo =
            json!({"success":"OK","payload":{"items":items,"paginacion":{"total_paginas":2,"numero_pagina":pagina,"tamano_pagina":50,"total_resultados":100}}});
        return responder(s, 200, &cuerpo.to_string());
    }

    let t = q.get("ticket").cloned().unwrap_or_default();
    if t.starts_with("malo") {
        return responder(s, 203, r#"{"Codigo":203,"Mensaje":"Ticket no válido."}"#);
    }
    if t.starts_with("simult") && contar(&format!("{t}{path}")) < 2 {
        return responder(s, 200, r#"{"Codigo":10500,"Mensaje":"Hemos detectado que existen peticiones simultáneas"}"#);
    }
    if t.starts_with("r429") && contar(&format!("{t}{path}")) < 1 {
        return responder(s, 429, "Too Many Requests");
    }
    if path.ends_with("/Empresas/BuscarComprador") {
        return responder(
            s,
            200,
            r#"{"Cantidad":2,"listaEmpresas":[{"CodigoEmpresa":"1","NombreEmpresa":"A"},{"CodigoEmpresa":"2","NombreEmpresa":"B"}]}"#,
        );
    }
    if path.ends_with("/Empresas/BuscarProveedor") {
        if q.get("rutempresaproveedor").map(String::as_str) == Some("0-0") {
            return responder(s, 500, r#"{"Codigo":10200,"Mensaje":"No hay resultados de empresas."}"#);
        }
        return responder(s, 200, r#"{"Cantidad":1,"listaEmpresas":[{"CodigoEmpresa":"25638","NombreEmpresa":"LATAM"}]}"#);
    }
    if path.ends_with("/ordenesdecompra.json") {
        return responder(
            s,
            200,
            r#"{"Cantidad":4,"Listado":[{"Codigo":"1-1-SE26"},{"Codigo":"2-2-CM26"},{"Codigo":"3-3-CM25"},{"Codigo":"4-4-CM2"}]}"#,
        );
    }
    responder(s, 200, &json!({"Cantidad":1,"Listado":[{"CodigoExterno":"1-L1","q":q}]}).to_string());
}

fn puerto() -> u16 {
    *PUERTO.get_or_init(|| {
        let l = TcpListener::bind("127.0.0.1:0").unwrap();
        let p = l.local_addr().unwrap().port();
        thread::spawn(move || {
            for s in l.incoming().flatten() {
                thread::spawn(move || atender(s));
            }
        });
        p
    })
}

fn nuevo(ticket: &str) -> Cliente {
    let p = puerto();
    let mut o = Opciones::nuevo(format!("{ticket}{:x}", rand_id())).unwrap();
    o.url_base_v1 = format!("http://127.0.0.1:{p}/v1/publico");
    o.url_base_v2 = format!("http://127.0.0.1:{p}/v2/compra-agil");
    o.timeout_segundos = 10;
    o.max_intentos = 3;
    o.espera_reintento_ms = 1;
    o.intervalo_minimo_ms = 0;
    Cliente::nuevo(o).unwrap()
}

fn rand_id() -> u64 {
    use std::time::{SystemTime, UNIX_EPOCH};
    static N: Mutex<u64> = Mutex::new(0);
    let mut n = N.lock().unwrap();
    *n += 1;
    SystemTime::now().duration_since(UNIX_EPOCH).unwrap().subsec_nanos() as u64 ^ (*n << 32)
}

fn s<'a>(v: &'a Value, k: &str) -> &'a str {
    v.get(k).and_then(Value::as_str).unwrap_or("")
}

#[test]
fn fecha_en_formato_ddmmaaaa() {
    assert_eq!(formato(NaiveDate::from_ymd_opt(2026, 3, 5).unwrap()), "05032026");
    let _ = hoy();
}

#[test]
fn licitaciones_por_fecha_manda_ticket_y_filtros() {
    let r = nuevo("ok").licitaciones(&Filtros {
        fecha: NaiveDate::from_ymd_opt(2026, 3, 5),
        estado: Some("adjudicada".into()),
        codigo_organismo: Some("7314".into()),
        ..Default::default()
    });
    assert!(r.exito);
    let q = &r.items[0]["q"];
    assert_eq!(s(q, "fecha"), "05032026");
    assert_eq!(s(q, "estado"), "adjudicada");
    assert_eq!(s(q, "CodigoOrganismo"), "7314");
}

#[test]
fn reintenta_peticiones_simultaneas_y_termina_bien() {
    let r = nuevo("simult").licitacion("1-L1");
    assert!(r.exito, "éxito tras reintentos: {}", r.mensaje);
    assert_eq!(s(r.primero().unwrap(), "CodigoExterno"), "1-L1");
}

#[test]
fn reintenta_http_429() {
    assert!(nuevo("r429").licitaciones_activas().exito);
}

#[test]
fn ticket_invalido_no_se_reintenta_y_devuelve_el_mensaje_oficial() {
    let r = nuevo("malo").licitaciones_activas();
    assert!(!r.exito);
    assert_eq!(r.codigo_error, Some(203));
    assert_eq!(r.mensaje, "Ticket no válido.");
}

#[test]
fn sin_filtros_validos_falla_localmente() {
    assert!(!nuevo("ok").licitaciones(&Filtros::default()).exito);
}

#[test]
fn convenio_marco_filtra_solo_los_codigos_cm_anio() {
    let r = nuevo("ok").ordenes_convenio_marco(NaiveDate::from_ymd_opt(2026, 3, 5).unwrap(), &Filtros::default());
    let codigos: Vec<&str> = r.items.iter().map(|i| s(i, "Codigo")).collect();
    assert_eq!(codigos, vec!["2-2-CM26", "3-3-CM25"]);
    assert!(es_convenio_marco("1002584-358-CM26"));
    assert!(!es_convenio_marco("1-1-SE26"));
    assert!(!es_convenio_marco("CM"));
}

#[test]
fn compradores_y_proveedores() {
    let c = nuevo("ok");
    assert_eq!(c.compradores().items.len(), 2);
    assert_eq!(s(c.proveedores("89.862.200-2").primero().unwrap(), "NombreEmpresa"), "LATAM");
}

#[test]
fn proveedor_sin_resultados_es_exito_con_lista_vacia() {
    let r = nuevo("ok").proveedores("0-0");
    assert!(r.exito);
    assert!(r.items.is_empty());
}

#[test]
fn compra_agil_ticket_por_cabecera_y_user_agent() {
    let r = nuevo("ok").compra_agil("3754-146-COT26");
    assert!(r.exito);
    assert_eq!(s(r.primero().unwrap(), "codigo"), "3754-146-COT26");
}

#[test]
fn compra_agil_codigo_invalido_devuelve_el_mensaje_de_la_api() {
    let r = nuevo("ok").compra_agil("MALO");
    assert!(!r.exito);
    assert_eq!(r.http, Some(400));
}

#[test]
fn compra_agil_recorre_las_16_regiones_y_deduplica() {
    let r = nuevo("ok").compra_agil_publicadas(&OpcionesCompraAgil::default());
    assert!(r.exito && !r.truncado);
    assert_eq!(r.items.len(), 16 * 2 + 1, "16 regiones × 2 páginas + 1 repetida única");
    assert_eq!(r.items.iter().filter(|i| s(i, "codigo") == "REPETIDA").count(), 1);
}

#[test]
fn compra_agil_palabra_clave_viaja_con_20_y_sin_recorrer_regiones() {
    let r = nuevo("ok").compra_agil_publicadas(&OpcionesCompraAgil {
        palabra_clave: Some("servicio aseo".into()),
        ..Default::default()
    });
    assert_eq!(s(&r.items[0], "q"), "servicio aseo");
    assert!(s(&r.items[0], "raw").contains("q=servicio%20aseo"), "espacio como %20, no +");
    assert_eq!(r.items.len(), 2, "una sola consulta paginada (2 páginas)");
}

#[test]
fn compra_agil_pagina_que_falla_deja_el_listado_truncado() {
    let r = nuevo("fallapag").compra_agil_publicadas(&OpcionesCompraAgil {
        regiones: vec![3],
        ..Default::default()
    });
    assert!(r.exito && r.truncado);
    assert_eq!(r.items.len(), 1);
}

#[test]
fn compra_agil_el_tope_de_tiempo_trunca_y_conserva_lo_acumulado() {
    let r = nuevo("lento").compra_agil_publicadas(&OpcionesCompraAgil {
        max_segundos: Some(1),
        ..Default::default()
    });
    assert!(r.truncado);
    assert!(!r.items.is_empty() && r.items.len() < 33);
}

#[test]
fn participantes_licitacion_adjudicada() {
    let d = json!({"CodigoExterno":"X","Comprador":{"NombreOrganismo":"HOSPITAL","RutUnidad":"61.602.148-6","CodigoOrganismo":"7314"},"Items":{"Listado":[
        {"Adjudicacion":{"RutProveedor":"78.723.570-0","NombreProveedor":"BARRON SPA"}},
        {"Adjudicacion":{"RutProveedor":"78.723.570-0","NombreProveedor":"BARRON SPA"}},
        {"Correlativo":3}]}});
    let p = de(&d);
    assert_eq!(p.compradores[0].nombre.as_deref(), Some("HOSPITAL"));
    assert_eq!(p.vendedores.len(), 1, "proveedor repetido una sola vez");
    assert_eq!(p.vendedores[0].rol, "vendedor");
}

#[test]
fn participantes_orden_de_compra_y_compra_agil() {
    let oc = de(&json!({"Codigo":"1-CM26","Comprador":{"NombreOrganismo":"DEP"},"Proveedor":{"Nombre":"LATAM","RutSucursal":"89.862.200-2"}}));
    assert_eq!(oc.vendedores[0].nombre.as_deref(), Some("LATAM"));
    assert_eq!(oc.compradores[0].fuente, "orden_compra");
    let ca = de(nuevo("ok").compra_agil("X-1-COT26").primero().unwrap());
    assert_eq!(ca.compradores[0].nombre.as_deref(), Some("MUNI X"));
    assert_eq!(ca.vendedores[0].nombre.as_deref(), Some("PROV UNO"));
}

#[test]
fn el_ticket_vacio_se_rechaza() {
    assert!(Opciones::nuevo("  ").is_err());
}

/// Prueba VIVA contra la API real (solo lectura). Requiere MERCADOPUBLICO_TICKET:
/// `MERCADOPUBLICO_TICKET=... cargo test -- --ignored --nocapture`
#[test]
#[ignore = "consulta la API real; requiere MERCADOPUBLICO_TICKET"]
fn vivo_contra_la_api_real() {
    let ticket = std::env::var("MERCADOPUBLICO_TICKET").expect("defina MERCADOPUBLICO_TICKET");
    let c = Cliente::nuevo(Opciones::nuevo(ticket).unwrap()).unwrap();

    let r = c.licitaciones_activas();
    assert!(r.exito && r.items.len() > 1000, "licitaciones activas: {}", r.items.len());

    let p = de(c.licitacion("2107-152-LE26").primero().expect("licitación"));
    assert!(
        !p.compradores.is_empty() && !p.vendedores.is_empty(),
        "licitación adjudicada sin comprador o vendedor"
    );
    println!("licitación: {:?} ← {:?}", p.compradores[0].nombre, p.vendedores[0].nombre);

    let ayer = hoy().pred_opt().unwrap();
    let r = c.ordenes_convenio_marco(ayer, &Filtros::default());
    assert!(r.exito && r.items.len() > 500, "convenios marco: {}", r.items.len());

    let p = de(c.orden_compra("1002584-358-CM26").primero().expect("orden"));
    assert_eq!(p.vendedores[0].rut.as_deref(), Some("89.862.200-2"));
    println!("orden: {:?} ← {:?}", p.compradores[0].nombre, p.vendedores[0].nombre);

    assert!(c.compradores().items.len() > 800);
    assert_eq!(s(c.proveedores("89.862.200-2").primero().unwrap(), "CodigoEmpresa"), "25638");

    let r = c.compra_agil_publicadas(&OpcionesCompraAgil {
        regiones: vec![15],
        ..Default::default()
    });
    assert!(r.exito && !r.truncado, "región 15: {}", r.mensaje);
    assert_eq!(Some(r.items.len() as u64), r.total, "completa");
    let d = c.compra_agil(s(&r.items[0], "codigo"));
    assert!(!de(d.primero().unwrap()).compradores.is_empty());
    println!("compra ágil región 15: {} de {:?}", r.items.len(), r.total);
}
