# ⚡ EcoFlow MQTT API

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow?style=for-the-badge)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com)
[![Container](https://img.shields.io/badge/ghcr.io-caunt%2Fecoflow--mqtt--api-2496ED?style=for-the-badge&logo=docker)](https://ghcr.io/caunt/ecoflow-mqtt-api)
[![Publish Builds](https://img.shields.io/github/actions/workflow/status/caunt/EcoFlow-MQTT-API/publish-builds.yml?style=for-the-badge&label=builds)](https://github.com/caunt/EcoFlow-MQTT-API/actions/workflows/publish-builds.yml)
[![Publish Container](https://img.shields.io/github/actions/workflow/status/caunt/EcoFlow-MQTT-API/publish-container.yml?style=for-the-badge&label=container)](https://github.com/caunt/EcoFlow-MQTT-API/actions/workflows/publish-container.yml)

Bridges EcoFlow devices to a local HTTP API via MQTT.

---

## 📥 Installation

Download the latest binary from the [releases page](https://github.com/caunt/EcoFlow-MQTT-API/releases/latest) *(coming soon)*.

| OS | Architectures |
|----|---------------|
| 🪟 Windows | `x64` · `arm64` · `x86` |
| 🍎 macOS | `arm64` · `x64` |
| 🐧 Linux (glibc) | `x64` · `arm64` · `arm` |
| 🐧 Linux (musl/Alpine) | `x64` · `arm64` |

---

## 🔐 Configuration

All settings are **environment variables** prefixed with `ECOFLOW_`.

### Authentication

Choose **one** (or both — App auth takes priority):

| Method | Variables |
|--------|-----------|
| 📱 **App** *(preferred)* | `ECOFLOW_USERNAME` + `ECOFLOW_PASSWORD` |
| 🔑 **Open API** | `ECOFLOW_ACCESS_KEY` + `ECOFLOW_SECRET_KEY` |

### Optional overrides

| Variable | Default |
|----------|---------|
| `ECOFLOW_APP_API_URI` | `https://api.ecoflow.com` |
| `ECOFLOW_OPEN_API_URI` | `https://api-e.ecoflow.com` |

---

## 🚀 Usage

### Binary

```sh
ECOFLOW_USERNAME=you@example.com ECOFLOW_PASSWORD=secret ./EcoFlow.Mqtt.Api
```

### 🐳 Docker

```sh
docker run --rm \
  -e ECOFLOW_USERNAME=you@example.com \
  -e ECOFLOW_PASSWORD=secret \
  -p 8080:8080 \
  ghcr.io/caunt/ecoflow-mqtt-api
```

---

## 🌐 API

| Endpoint | Description |
|----------|-------------|
| `GET /` | All devices (JSON) |
| `GET /{serialNumber}` | Single device (JSON) |

Add `?flat` for plain-text `key=value` output:

```sh
curl http://localhost:8080/
curl http://localhost:8080/ABC123
curl "http://localhost:8080/ABC123?flat"
```
