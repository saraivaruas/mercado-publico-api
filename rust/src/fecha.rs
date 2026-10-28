use chrono::{NaiveDate, Utc};
use chrono_tz::America::Santiago;

/// La API entiende "hoy" como el día calendario de Chile. Un servidor en UTC pasa a "mañana" por la
/// tarde/noche de Chile y la API rechaza esa fecha, por eso nunca use la hora del servidor.
pub fn hoy() -> NaiveDate {
    Utc::now().with_timezone(&Santiago).date_naive()
}

/// Formato que exige la API: ddmmaaaa.
pub fn formato(fecha: NaiveDate) -> String {
    fecha.format("%d%m%Y").to_string()
}
