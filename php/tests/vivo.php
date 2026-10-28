<?php
// Prueba VIVA contra la API real (solo lectura). Requiere MERCADOPUBLICO_TICKET.
//   MERCADOPUBLICO_TICKET=... php tests/vivo.php
declare(strict_types=1);

spl_autoload_register(function (string $c): void {
    if (str_starts_with($c, 'MercadoPublico\\')) {
        require __DIR__ . '/../src/' . substr($c, 15) . '.php';
    }
});

use MercadoPublico\{Cliente, FechaChile, Opciones, Participantes};

$ticket = getenv('MERCADOPUBLICO_TICKET');
if (!$ticket) {
    fwrite(STDERR, "Defina MERCADOPUBLICO_TICKET.\n");
    exit(2);
}
$c = new Cliente(new Opciones($ticket));
$fallas = 0;
function ver(string $n, bool $ok, string $detalle = ''): void
{
    global $fallas;
    echo ($ok ? '  ✔ ' : '  ✘ ') . $n . ($detalle ? " — $detalle" : '') . "\n";
    $fallas += $ok ? 0 : 1;
}

echo "Mercado Público — prueba VIVA (PHP)\n";
$r = $c->licitacionesActivas();
ver('licitaciones activas', $r->exito && count($r->items) > 1000, count($r->items) . ' licitaciones');

$r = $c->licitacion('2107-152-LE26');
$p = $r->exito ? Participantes::de($r->primero()) : ['compradores' => [], 'vendedores' => []];
ver('licitación adjudicada: quién compra y quién vende', $p['compradores'] !== [] && $p['vendedores'] !== [], ($p['compradores'][0]->nombre ?? '?') . ' ← ' . ($p['vendedores'][0]->nombre ?? '?'));

$ayer = FechaChile::hoy()->modify('-1 day');
$r = $c->ordenesConvenioMarco($ayer);
ver('convenios marco de ayer', $r->exito && count($r->items) > 500, count($r->items) . ' órdenes CM');

$r = $c->ordenCompra('1002584-358-CM26');
$p = $r->exito ? Participantes::de($r->primero()) : ['compradores' => [], 'vendedores' => []];
ver('orden de compra: quién compra y quién vende', ($p['vendedores'][0]->rut ?? '') === '89.862.200-2', ($p['compradores'][0]->nombre ?? '?') . ' ← ' . ($p['vendedores'][0]->nombre ?? '?'));

$r = $c->compradores();
ver('compradores (organismos)', $r->exito && count($r->items) > 800, count($r->items) . ' organismos');
$r = $c->proveedores('89.862.200-2');
ver('proveedor por RUT', $r->exito && ($r->primero()['CodigoEmpresa'] ?? '') === '25638', $r->primero()['NombreEmpresa'] ?? '?');

$r = $c->compraAgilPublicadas(['regiones' => [15]]);
$codigos = array_column($r->items, 'codigo');
ver('Compra Ágil región 15 completa', $r->exito && !$r->truncado && count($r->items) === $r->total && count($codigos) === count(array_unique($codigos)), count($r->items) . ' de ' . $r->total);
$d = $c->compraAgil((string) ($r->items[0]['codigo'] ?? ''));
$p = $d->exito ? Participantes::de($d->primero()) : ['compradores' => []];
ver('detalle de Compra Ágil: quién compra', $d->exito && $p['compradores'] !== [], $p['compradores'][0]->nombre ?? '?');

echo $fallas ? "\n$fallas con falla\n" : "\nTodo correcto contra la API real\n";
exit($fallas ? 1 : 0);
