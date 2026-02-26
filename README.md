# ⚡ EcoFlow HTTP API

[![Publish Builds](https://img.shields.io/github/actions/workflow/status/caunt/EcoFlow-MQTT-API/publish-builds.yml?style=for-the-badge&label=builds)](https://github.com/caunt/EcoFlow-MQTT-API/actions/workflows/publish-builds.yml)
[![Downloads Count](https://img.shields.io/github/downloads/caunt/EcoFlow-MQTT-API/latest/total?style=for-the-badge)](https://github.com/caunt/EcoFlow-MQTT-API/releases/latest)  
[![Publish Container](https://img.shields.io/github/actions/workflow/status/caunt/EcoFlow-MQTT-API/publish-container.yml?style=for-the-badge&label=container)](https://github.com/caunt/EcoFlow-MQTT-API/actions/workflows/publish-container.yml)
[![Container](https://img.shields.io/badge/ghcr.io-caunt%2Fecoflow--mqtt--api-2496ED?style=for-the-badge&logo=docker)](https://ghcr.io/caunt/ecoflow-mqtt-api)

Exposes EcoFlow devices status as HTTP API.

---

## 📥 Installation

Download the latest binary from the [releases page](https://github.com/caunt/EcoFlow-MQTT-API/releases/latest).

| OS | x64 | arm64 | x86 | arm |
|:--:|:---:|:-----:|:---:|:---:|
| 🪟 Windows             | ✅ | ✅ | ✅ | 🚫 |
| 🍏 macOS               | ✅ | ✅ | 🚫 | 🚫 |
| 🐧 Linux (glibc)       | ✅ | ✅ | 🚫 | ✅ |
| 🐧 Linux (musl/Alpine) | ✅ | ✅ | 🚫 | 🚫 |

---

## 🚀 Usage

### 🐳 Docker

```sh
docker run --rm --pull=always \
  -e ECOFLOW_USERNAME=you@example.com \
  -e ECOFLOW_PASSWORD=your_password \
  -p 8080:8080 \
  ghcr.io/caunt/ecoflow-mqtt-api
```

### 💾 Binary

```sh
ECOFLOW_USERNAME="you@example.com" ECOFLOW_PASSWORD="your_password" ./EcoFlow.Mqtt.Api
```

---

## 🔐 Configuration

Set environment variables to configure.

### Authentication

Choose **one**:

| Method | Variables |
|--------|-----------|
| 📱 **App** *(preferred)* | `ECOFLOW_USERNAME` + `ECOFLOW_PASSWORD` |
| 🔑 **Open API** | `ECOFLOW_ACCESS_KEY` + `ECOFLOW_SECRET_KEY` |

### Optional overrides

#### EcoFlow

| Variable | Default |
|----------|---------|
| `ECOFLOW_APP_API_URI` | `https://api.ecoflow.com` |
| `ECOFLOW_OPEN_API_URI` | `https://api-e.ecoflow.com` |

#### Web Server

| Variable | Default | Example |
|----------|---------|---------|
| `URLS` | `http://localhost:8080;` | `http://*:8080;` |
| `HTTP_PORTS` | `‎ ` | `8080;` |

---

## 🌐 API

| Endpoint | Description |
|----------|-------------|
| `GET /` | All devices |
| `GET /{serialNumber}` | Single device |

Add `?flat` for plain-text `key=value` output.
