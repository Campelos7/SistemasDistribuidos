#!/usr/bin/env python3
"""
Sensor urbano em Python — publica no mesmo RabbitMQ que o sensor C#.
Demonstra interoperabilidade: o Gateway .NET consome mensagens JSON
com o mesmo contrato (MensagemPubSub), sem acoplamento de linguagem.

Uso:
  python sensor.py [sensorId] [zona] [tipos separados por virgula]

Exemplo:
  python sensor.py S201 ZONA_ESCOLAR PM2.5,TEMP,HUM
"""

from __future__ import annotations

import json
import os
import sys
import threading
import time
from datetime import datetime

import pika

EXCHANGE = "monitorizacao.urbana"
HEARTBEAT_SEGUNDOS = 30


def normalizar(valor: str) -> str:
    return valor.replace(" ", "_").upper()


def routing_medicao(zona: str, tipo_dado: str) -> str:
    return f"medicao.{normalizar(zona)}.{normalizar(tipo_dado)}"


def routing_heartbeat(zona: str, sensor_id: str) -> str:
    return f"heartbeat.{normalizar(zona)}.{normalizar(sensor_id)}"


def routing_registo(zona: str, sensor_id: str) -> str:
    return f"registo.{normalizar(zona)}.{normalizar(sensor_id)}"


def timestamp_agora() -> str:
    return datetime.now().strftime("%Y-%m-%dT%H:%M:%S")


def publicar(canal: pika.adapters.blocking_connection.BlockingChannel, routing_key: str, msg: dict) -> None:
    corpo = json.dumps(msg, ensure_ascii=False)
    canal.basic_publish(
        exchange=EXCHANGE,
        routing_key=routing_key,
        body=corpo.encode("utf-8"),
        properties=pika.BasicProperties(
            content_type="application/json",
            delivery_mode=2,
        ),
    )


def ligar_rabbit() -> tuple[pika.BlockingConnection, pika.adapters.blocking_connection.BlockingChannel]:
    host = os.environ.get("RABBIT_HOST", "localhost")
    user = os.environ.get("RABBIT_USER", "guest")
    password = os.environ.get("RABBIT_PASS", "guest")

    parametros = pika.ConnectionParameters(
        host=host,
        credentials=pika.PlainCredentials(user, password),
    )
    ligacao = pika.BlockingConnection(parametros)
    canal = ligacao.channel()
    canal.exchange_declare(
        exchange=EXCHANGE,
        exchange_type="topic",
        durable=True,
        auto_delete=False,
    )
    return ligacao, canal


def publicar_registo(canal, sensor_id: str, zona: str, tipos: list[str]) -> None:
    msg = {
        "Tipo": "registo",
        "SensorId": sensor_id,
        "Zona": zona,
        "TiposSuportados": tipos,
        "Timestamp": timestamp_agora(),
    }
    publicar(canal, routing_registo(zona, sensor_id), msg)
    print("[SENSOR-PY] Registo publicado no broker.")


def publicar_medicao(canal, sensor_id: str, zona: str, tipo: str, valor: float) -> None:
    msg = {
        "Tipo": "medicao",
        "SensorId": sensor_id,
        "Zona": zona,
        "TipoDado": tipo,
        "Valor": valor,
        "Timestamp": timestamp_agora(),
        "Formato": "NONE",
    }
    publicar(canal, routing_medicao(zona, tipo), msg)
    print(f"[SENSOR-PY] Medição publicada: {tipo}={valor}")


def publicar_heartbeat(canal, sensor_id: str, zona: str) -> None:
    msg = {
        "Tipo": "heartbeat",
        "SensorId": sensor_id,
        "Zona": zona,
        "Timestamp": timestamp_agora(),
    }
    publicar(canal, routing_heartbeat(zona, sensor_id), msg)
    print("[SENSOR-PY] Heartbeat publicado.")


def iniciar_heartbeat(canal, sensor_id: str, zona: str, parar: threading.Event) -> None:
    def loop() -> None:
        while not parar.wait(HEARTBEAT_SEGUNDOS):
            try:
                publicar_heartbeat(canal, sensor_id, zona)
            except Exception as ex:
                print(f"[SENSOR-PY] Erro no heartbeat: {ex}")
                break

    threading.Thread(target=loop, daemon=True).start()


def parse_args(argv: list[str]) -> tuple[str, str, list[str]]:
    sensor_id = argv[0] if len(argv) > 0 else "S201"
    zona = argv[1] if len(argv) > 1 else "ZONA_ESCOLAR"
    tipos_raw = argv[2] if len(argv) > 2 else "PM2.5,TEMP,HUM"
    tipos = [t.strip() for t in tipos_raw.split(",") if t.strip()]
    return sensor_id, zona, tipos


def main() -> None:
    sensor_id, zona, tipos = parse_args(sys.argv[1:])

    ligacao, canal = ligar_rabbit()
    publicar_registo(canal, sensor_id, zona, tipos)

    parar = threading.Event()
    iniciar_heartbeat(canal, sensor_id, zona, parar)

    print(f"[SENSOR-PY {sensor_id}] Ligado ao RabbitMQ. Zona: {zona}")
    print("\nComandos:")
    print("  data <tipo> <valor>   (ex: data TEMP 22)")
    print("  bye")

    try:
        while True:
            try:
                linha = input("> ").strip()
            except EOFError:
                break

            if not linha:
                continue

            partes = linha.split()
            comando = partes[0].lower()

            if comando == "bye":
                break

            if comando == "data" and len(partes) == 3:
                tipo = partes[1]
                try:
                    valor = float(partes[2].replace(",", "."))
                except ValueError:
                    print("[SENSOR-PY] Valor inválido.")
                    continue
                publicar_medicao(canal, sensor_id, zona, tipo, valor)
                continue

            print("[SENSOR-PY] Comando inválido.")
    finally:
        parar.set()
        canal.close()
        ligacao.close()
        print("[SENSOR-PY] Encerrado.")


if __name__ == "__main__":
    main()
