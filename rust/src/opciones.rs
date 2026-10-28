/// Configuración del cliente. Los tiempos van en milisegundos salvo `timeout_segundos`.
#[derive(Debug, Clone)]
pub struct Opciones {
    /// Ticket de la API (se pide gratis en api.mercadopublico.cl). Nunca lo deje escrito en el código.
    pub ticket: String,
    /// Base de la API v1 (licitaciones, órdenes de compra, empresas).
    pub url_base_v1: String,
    /// Base de la API v2 (Compra Ágil).
    pub url_base_v2: String,
    pub timeout_segundos: u64,
    /// Intentos totales por consulta ante errores transitorios (peticiones simultáneas, 429, 5xx, red).
    pub max_intentos: u32,
    /// Espera base entre reintentos; el intento N espera N veces este valor.
    pub espera_reintento_ms: u64,
    /// Separación mínima entre dos consultas. Sin ella la API responde "peticiones simultáneas" o 429.
    pub intervalo_minimo_ms: u64,
    /// Sin User-Agent el WAF de la API v2 responde 403.
    pub user_agent: String,
}

impl Opciones {
    /// Opciones por defecto con el ticket dado. Falla si el ticket está vacío.
    pub fn nuevo(ticket: impl Into<String>) -> Result<Self, String> {
        let ticket = ticket.into();
        if ticket.trim().is_empty() {
            return Err("El ticket no puede estar vacío.".into());
        }
        Ok(Self {
            ticket,
            url_base_v1: "https://api.mercadopublico.cl/servicios/v1/publico".into(),
            url_base_v2: "https://api2.mercadopublico.cl/v2/compra-agil".into(),
            timeout_segundos: 45,
            max_intentos: 4,
            espera_reintento_ms: 2000,
            intervalo_minimo_ms: 1000,
            user_agent: "Mozilla/5.0 (compatible; mercado-publico-api/1.0; +https://github.com/saraivaruas/mercado-publico-api)".into(),
        })
    }
}
