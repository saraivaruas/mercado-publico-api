use serde_json::Value;

/// Resultado de una consulta (ver la documentación del crate).
#[derive(Debug, Clone)]
pub struct Resultado {
    pub exito: bool,
    pub mensaje: String,
    pub items: Vec<Value>,
    /// Código de error de la API (ej. 203 ticket inválido, 10500 peticiones simultáneas) o HTTP en Compra Ágil.
    pub codigo_error: Option<i64>,
    pub http: Option<u16>,
    /// Verdadero si el listado es PARCIAL (una página falló o se agotó el tiempo): faltan registros.
    pub truncado: bool,
    /// Total declarado por la API, si lo informa.
    pub total: Option<u64>,
}

impl Resultado {
    /// Primer elemento (útil en consultas por código).
    pub fn primero(&self) -> Option<&Value> {
        self.items.first()
    }

    pub(crate) fn ok(items: Vec<Value>, total: Option<u64>, truncado: bool, mensaje: impl Into<String>, http: Option<u16>) -> Self {
        Self {
            exito: true,
            mensaje: mensaje.into(),
            items,
            codigo_error: None,
            http,
            truncado,
            total,
        }
    }

    pub(crate) fn error(mensaje: impl Into<String>, codigo_error: Option<i64>, http: Option<u16>) -> Self {
        Self {
            exito: false,
            mensaje: mensaje.into(),
            items: vec![],
            codigo_error,
            http,
            truncado: false,
            total: None,
        }
    }
}
