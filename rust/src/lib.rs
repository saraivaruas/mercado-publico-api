//! Cliente de las APIs públicas de Mercado Público (ChileCompra).
//!
//! Mercado Público es la plataforma de compras de la Dirección ChileCompra: este cliente
//! cubre sus dos APIs con el mismo ticket.
//!
//! * v1 (`api.mercadopublico.cl`): licitaciones, órdenes de compra (incluye Convenio Marco),
//!   compradores y proveedores.
//! * v2 (`api2.mercadopublico.cl`): Compra Ágil.
//!
//! La API pide ~1 consulta por segundo (si no, responde "peticiones simultáneas" o 429): el
//! cliente espacia las consultas y reintenta los errores transitorios. Compra Ágil solo pagina
//! hasta 10.000 resultados por consulta y hay más publicadas, por eso se recorre por región.
//!
//! Las fallas de la API NO son `Err`: se devuelven en [`Resultado`] con `exito = false` y el
//! mensaje oficial. Los elementos se entregan como [`serde_json::Value`] tal cual los devuelve
//! la API, porque su forma no está garantizada.

mod cliente;
mod fecha;
mod opciones;
mod participantes;
mod resultado;

pub use cliente::{es_convenio_marco, Cliente, Filtros, OpcionesCompraAgil};
pub use fecha::{formato, hoy};
pub use opciones::Opciones;
pub use participantes::{de, de_compra_agil, de_licitacion, de_orden_compra, Parte, QuienCompraQuienVende};
pub use resultado::Resultado;
