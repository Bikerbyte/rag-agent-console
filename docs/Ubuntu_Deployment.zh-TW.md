# Ubuntu VM 部署說明

這份部署包的目標很單純：

- 同一份程式可部署到單機或多台 Ubuntu VM
- 使用 `systemd` 管服務
- 使用 `Nginx` 做 reverse proxy
- 用 `.env` 決定每台節點的 runtime role
- 讓 webhook、queue worker、leadership lease 都能在 VM 上直接驗證

---

## 目錄位置

- `deploy/ubuntu/cpbl-telegram-assistant.service`
- `deploy/ubuntu/install-or-update.sh`
- `deploy/ubuntu/cpbl-telegram-assistant.env.example`
- `deploy/ubuntu/cpbl-telegram-assistant.single-node.env.example`
- `deploy/ubuntu/cpbl-telegram-assistant.primary.env.example`
- `deploy/ubuntu/cpbl-telegram-assistant.scale-node.env.example`
- `deploy/nginx/cpbl-telegram-assistant.conf`

---

## 前置需求

### Ubuntu 套件

請先安裝：

- `aspnetcore-runtime-10.0` 或對應的 .NET 10 runtime
- `nginx`
- `postgresql-client`

如果你要在 VM 上直接 `dotnet publish`，還需要安裝 .NET SDK。

### 建議路徑

- app publish 路徑：`/opt/cpbl-telegram-assistant/app`
- env 檔：`/etc/cpbl-telegram-assistant/cpbl-telegram-assistant.env`
- systemd service：`/etc/systemd/system/cpbl-telegram-assistant.service`

---

## 單機部署

### 1. 複製專案並進到 repo

```bash
git clone <your-repo-url>
cd CPBLLineBotCloud
```

### 2. 執行安裝腳本

```bash
sudo bash deploy/ubuntu/install-or-update.sh
```

### 3. 套用單機 env

```bash
sudo cp deploy/ubuntu/cpbl-telegram-assistant.single-node.env.example /etc/cpbl-telegram-assistant/cpbl-telegram-assistant.env
sudo nano /etc/cpbl-telegram-assistant/cpbl-telegram-assistant.env
```

至少要補：

- `ConnectionStrings__DefaultConnection`
- `TelegramBot__BotToken`
- `TelegramBot__WebhookUrl`
- `TelegramBot__WebhookSecretToken`

### 4. 重啟服務

```bash
sudo systemctl restart cpbl-telegram-assistant
sudo systemctl status cpbl-telegram-assistant --no-pager
```

---

## 多節點部署

### Node A

套用：

```bash
sudo cp deploy/ubuntu/cpbl-telegram-assistant.primary.env.example /etc/cpbl-telegram-assistant/cpbl-telegram-assistant.env
```

### Node B ~ Node N

套用：

```bash
sudo cp deploy/ubuntu/cpbl-telegram-assistant.scale-node.env.example /etc/cpbl-telegram-assistant/cpbl-telegram-assistant.env
```

### 多節點時要注意

- 所有節點都要指向同一個 PostgreSQL
- 所有節點都要設定不同的 `AppRuntime__InstanceName`
- 所有節點都可以開：
  - `AppRuntime__EnableTelegramWebhookIngress=true`
  - `AppRuntime__EnableTelegramUpdateQueueWorker=true`
  - `AppRuntime__EnableOfficialDataSyncWorker=true`
  - `AppRuntime__EnableNotificationWorker=true`
- 由 lease 決定哪台節點當前執行 scheduled jobs

這樣即使不是固定 primary，也能自動接手。

---

## Nginx 設定

範本在：

- `deploy/nginx/cpbl-telegram-assistant.conf`

建議做法：

```bash
sudo cp deploy/nginx/cpbl-telegram-assistant.conf /etc/nginx/sites-available/cpbl-telegram-assistant.conf
sudo ln -sf /etc/nginx/sites-available/cpbl-telegram-assistant.conf /etc/nginx/sites-enabled/cpbl-telegram-assistant.conf
sudo nginx -t
sudo systemctl reload nginx
```

如果你有正式網域，請再搭配 Let's Encrypt 或其他 TLS termination 做 HTTPS。

---

## 資料庫 migration

第一次部署或 schema 有更新時，先跑：

```bash
dotnet ef database update
```

如果 VM 上沒有 SDK，可以在 CI 或本機先處理 migration，再讓 app 啟動時自動 migrate。

---

## 驗證清單

### 1. 看 systemd 狀態

```bash
sudo systemctl status cpbl-telegram-assistant --no-pager
```

### 2. 看最近 log

```bash
sudo journalctl -u cpbl-telegram-assistant -n 100 --no-pager
```

### 3. 看 runtime endpoint

```bash
curl http://127.0.0.1:5050/api/runtime
```

### 4. 看 Telegram health

```bash
curl http://127.0.0.1:5050/api/telegram/health
```

### 5. 看後台 Runtime 頁

確認：

- node heartbeat 是否出現
- leadership lease 是否有 owner
- queue 是否有正常流動

---

## 常見操作

### 更新程式

```bash
git pull
sudo bash deploy/ubuntu/install-or-update.sh
```

### 重啟服務

```bash
sudo systemctl restart cpbl-telegram-assistant
```

### 停止服務

```bash
sudo systemctl stop cpbl-telegram-assistant
```

### 查看 env

```bash
sudo cat /etc/cpbl-telegram-assistant/cpbl-telegram-assistant.env
```

---

## 目前這版部署包的定位

這不是完整的平台化部署系統，也不是 K8s 替代品。

這包的定位比較像：

- 讓 side project 可以在 Ubuntu VM 上穩定跑
- 讓多節點 runtime role 與 leadership lease 能實際驗證
- 讓 demo、面試與日常維護都比較順手
