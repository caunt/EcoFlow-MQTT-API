# ⚡ EcoFlow MQTT API

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Container](https://img.shields.io/badge/ghcr.io-caunt%2Fecoflow--mqtt--api-blue?logo=docker)](https://ghcr.io/caunt/ecoflow-mqtt-api)

A lightweight bridge that authenticates with the EcoFlow cloud, subscribes to your devices over MQTT, and exposes their live state as a local HTTP API.

---

## 📥 Installation

**Download the latest pre-built binary** for your platform from the [latest release](https://github.com/caunt/EcoFlow-MQTT-API/releases/latest) *(no release yet — coming soon)*.

Supported platforms: `win-x64` · `win-arm64` · `win-x86` · `osx-arm64` · `osx-x64` · `linux-x64` · `linux-arm64` · `linux-arm` · `linux-musl-x64` · `linux-musl-arm64`

Or pull the container image:

```sh
docker pull ghcr.io/caunt/ecoflow-mqtt-api
```

---

## 🔐 Configuration

All settings are passed as **environment variables** prefixed with `ECOFLOW_`.

### Authentication

Choose **one** of the two methods (or provide both — App auth takes priority):

| Method | Variables |
|--------|-----------|
| 🔑 **Open API** (recommended) | `ECOFLOW_ACCESS_KEY` + `ECOFLOW_SECRET_KEY` |
| 📱 **App** (username/password) | `ECOFLOW_USERNAME` + `ECOFLOW_PASSWORD` |

### Optional overrides

| Variable | Default |
|----------|---------|
| `ECOFLOW_APP_API_URI` | `https://api.ecoflow.com` |
| `ECOFLOW_OPEN_API_URI` | `https://api-e.ecoflow.com` |

---

## 🚀 Usage

### Binary

```sh
ECOFLOW_ACCESS_KEY=your_key ECOFLOW_SECRET_KEY=your_secret ./EcoFlow.Mqtt.Api
```

The service listens on port **8080** by default and prints discovered devices on startup.

### 🐳 Docker

```sh
docker run --rm \
  -e ECOFLOW_ACCESS_KEY=your_key \
  -e ECOFLOW_SECRET_KEY=your_secret \
  -p 8080:8080 \
  ghcr.io/caunt/ecoflow-mqtt-api
```

---

## 🌐 API Endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /` | Live state of **all** devices as JSON |
| `GET /{serialNumber}` | Live state of a **single** device |

Append `?flat` to any endpoint for a flat `key=value` plain-text response, useful for simple integrations:

```sh
curl http://localhost:8080/                          # all devices (JSON)
curl http://localhost:8080/ABC123                    # single device (JSON)
curl "http://localhost:8080/ABC123?flat"             # single device (flat)
```

---

## 📄 License

[MIT](LICENSE)
