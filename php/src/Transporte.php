<?php

declare(strict_types=1);

namespace MercadoPublico;

/**
 * Capa HTTP mínima (cURL): una consulta GET con separación mínima entre
 * consultas. No interpreta respuestas; eso lo hace el Cliente.
 *
 * @internal
 */
final class Transporte
{
    private float $ultimaConsulta = 0.0;

    public function __construct(private readonly Opciones $opciones)
    {
    }

    /**
     * @param string[] $cabeceras
     * @return array{http: int, cuerpo: string, errorRed: string}
     */
    public function get(string $url, array $cabeceras = []): array
    {
        $this->respetarIntervalo();

        $ch = curl_init($url);
        curl_setopt_array($ch, [
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_TIMEOUT => $this->opciones->timeoutSegundos,
            CURLOPT_CONNECTTIMEOUT => min(15, $this->opciones->timeoutSegundos),
            CURLOPT_USERAGENT => $this->opciones->userAgent,
            CURLOPT_HTTPHEADER => array_merge(['Accept: application/json'], $cabeceras),
        ]);
        $cuerpo = curl_exec($ch);
        $error = curl_error($ch);
        $http = (int) curl_getinfo($ch, CURLINFO_HTTP_CODE);
        curl_close($ch);
        $this->ultimaConsulta = microtime(true);

        return ['http' => $http, 'cuerpo' => $cuerpo === false ? '' : (string) $cuerpo, 'errorRed' => $error];
    }

    public function dormirMs(int $ms): void
    {
        if ($ms > 0) {
            usleep($ms * 1000);
        }
    }

    private function respetarIntervalo(): void
    {
        if ($this->ultimaConsulta > 0.0 && $this->opciones->intervaloMinimoMs > 0) {
            $falta = $this->opciones->intervaloMinimoMs / 1000 - (microtime(true) - $this->ultimaConsulta);
            if ($falta > 0) {
                usleep((int) ($falta * 1_000_000));
            }
        }
    }
}
