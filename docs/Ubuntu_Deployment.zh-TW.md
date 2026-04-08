# Ubuntu VM 部署說明

這份部署包的目標很單純：

- 同一份程式可部署到單機或多台 Ubuntu VM
- 可用 `systemd` 或 Docker 兩種方式落地
- 使用 `systemd` 管服務
- 使用 `Nginx` 做 reverse proxy
- 用 `.env` 決定每台節點的 runtime profile
- 讓 webhook、queue worker、leadership lease 都能在 VM 上直接驗證

---

## 目錄位置

- `deploy/ubuntu/cpbl-telegram-assistant.service`
- `deploy/ubuntu/install-or-update.sh`
- `deploy/ubuntu/cpbl-telegram-assistant.env.example`
- `deploy/ubuntu/cpbl-telegram-assistant.single-node.env.example`
- `deploy/ubuntu/cpbl-telegram-assistant.primary.env.example`
- `deploy/ubuntu/cpbl-telegram-assistant.scale-node.env.example`
- `deploy/ubuntu/check-runtime.sh`
- `deploy/ubuntu/check-leases.sh`
- `deploy/ubuntu/check-webhook.sh`
- `deploy/nginx/cpbl-telegram-assistant.conf`

---

## 前置需求

### Ubuntu 套件

如果你走 `systemd + dotnet publish`，請先安裝：

- `aspnetcore-runtime-10.0` 或對應的 .NET 10 runtime
- `nginx`
- `postgresql-client`

如果你要在 VM 上直接 `dotnet publish`，還需要安裝 .NET SDK。

如果你走 `Docker + Nginx`，請先安裝：

- `docker`
- `docker compose plugin`
- `nginx`
- `postgresql-client`

### 建議路徑

- app publish 路徑：`/opt/cpbl-telegram-assistant/app`
- env 檔：`/etc/cpbl-telegram-assistant/cpbl-telegram-assistant.env`
- systemd service：`/etc/systemd/system/cpbl-telegram-assistant.service`

---

## 推薦路線

如果你現在是要快速驗證：

- 兩台 Ubuntu VM
- Webhook ingress
- app 層 load balance
- queue worker 多節點協作
- leadership lease 自動接手

最推薦先走這條：

- `Local 主機`：PostgreSQL
- `VM1`：Docker app + Host Nginx
- `VM2`：Docker app

流量路徑：

- Telegram webhook
- 打到 `VM1` 的公開網址
- `VM1 Nginx` 再分流到：
  - `VM1 app container`
  - `VM2 app container`

這條路的重點是：

- app 用 Docker，部署快
- 兩台 app 都跑同一個 image
- 兩台都可設 `AppRuntime__Profile=Standard`
- `scheduled jobs` 由資料庫 lease 決定誰真正執行

注意：這是「app 層有 load balance」，但 ingress 仍由 `VM1 Nginx` 承接，所以入口本身還不是高可用。

---

## Docker 雙 VM 佈署

### 1. Local PostgreSQL 準備

如果你要先拿 local 主機當 PostgreSQL，至少要確認：

- `listen_addresses='*'`
- `pg_hba.conf` 已放行 `VM1`、`VM2` 的 IP
- 防火牆有開 `5432`
- 連線字串要填 local 主機的實際 IP，不要用 `localhost`

這適合測試，不適合長期正式部署。

### 2. 兩台 VM 安裝 Docker

兩台 Ubuntu VM 都可先安裝：

```bash
sudo apt-get update
sudo apt-get install -y ca-certificates curl gnupg nginx
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo usermod -aG docker $USER
```

如果 `VM2` 不需要主機版 Nginx，可以把 `nginx` 從安裝清單拿掉。

### 3. 兩台 VM 抓 repo 並 build image

```bash
git clone <your-repo-url>
cd CPBLLineBotCloud
git checkout dev
docker build -t cpbl-telegram-assistant:latest .
```

### 4. VM1 的 env

建立 `/opt/cpbl/.env`：

```env
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
ConnectionStrings__DefaultConnection=Host=<LOCAL_DB_IP>;Port=5432;Database=cpbl_telegram_assistant;Username=postgres;Password=<PASSWORD>

TelegramBot__Enabled=true
TelegramBot__BotToken=<BOT_TOKEN>
TelegramBot__UseWebhookMode=true
TelegramBot__WebhookPath=/api/telegram/webhook
TelegramBot__WebhookUrl=https://<PUBLIC_HOST>/api/telegram/webhook
TelegramBot__WebhookSecretToken=<WEBHOOK_SECRET>

AppRuntime__Profile=Standard
AppRuntime__InstanceName=vm-1
AppRuntime__EnableLeadershipLease=true
```

啟動 container：

```bash
sudo mkdir -p /opt/cpbl
docker run -d \
  --name cpbl-telegram-assistant \
  --restart unless-stopped \
  --env-file /opt/cpbl/.env \
  -p 127.0.0.1:8080:8080 \
  cpbl-telegram-assistant:latest
```

### 5. VM2 的 env

建立 `/opt/cpbl/.env`：

```env
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
ConnectionStrings__DefaultConnection=Host=<LOCAL_DB_IP>;Port=5432;Database=cpbl_telegram_assistant;Username=postgres;Password=<PASSWORD>

TelegramBot__Enabled=true
TelegramBot__BotToken=<BOT_TOKEN>
TelegramBot__UseWebhookMode=true
TelegramBot__WebhookPath=/api/telegram/webhook
TelegramBot__WebhookUrl=https://<PUBLIC_HOST>/api/telegram/webhook
TelegramBot__WebhookSecretToken=<WEBHOOK_SECRET>

AppRuntime__Profile=Standard
AppRuntime__InstanceName=vm-2
AppRuntime__EnableLeadershipLease=true
```

啟動 container：

```bash
sudo mkdir -p /opt/cpbl
docker run -d \
  --name cpbl-telegram-assistant \
  --restart unless-stopped \
  --env-file /opt/cpbl/.env \
  -p 8080:8080 \
  cpbl-telegram-assistant:latest
```

### 6. 先確認兩台 app 都活著

VM1：

```bash
curl http://127.0.0.1:8080/api/runtime
```

VM2：

```bash
curl http://127.0.0.1:8080/api/runtime
```

你應該要看到：

- `Profile=Standard`
- `InstanceName` 不同

### 7. VM1 設定 Nginx upstream

建立 `/etc/nginx/sites-available/cpbl-telegram-assistant.conf`：

```nginx
upstream cpbl_app_nodes {
    server 127.0.0.1:8080;
    server <VM2_PRIVATE_IP>:8080;
}

server {
    listen 80;
    server_name <PUBLIC_HOST>;

    client_max_body_size 10m;

    location / {
        proxy_pass         http://cpbl_app_nodes;
        proxy_http_version 1.1;
        proxy_set_header   Host $host;
        proxy_set_header   X-Real-IP $remote_addr;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        proxy_read_timeout 120s;
        proxy_send_timeout 120s;
    }
}
```

啟用設定：

```bash
sudo ln -sf /etc/nginx/sites-available/cpbl-telegram-assistant.conf /etc/nginx/sites-enabled/cpbl-telegram-assistant.conf
sudo nginx -t
sudo systemctl reload nginx
```

### 8. 驗證分流與 lease

打幾次：

```bash
curl -I http://<PUBLIC_HOST>
```

看 response header 的 `X-App-Instance` 是否會出現：

- `vm-1`
- `vm-2`

再看 lease：

```bash
curl http://<PUBLIC_HOST>/api/runtime/leases
```

同一時間只會有一台節點持有：

- `OfficialDataSync`
- `TelegramNotification`

### 9. 最後才讓 Telegram webhook 指進來

當你確認：

- 兩台 app 都正常
- `X-App-Instance` 會輪流變動
- lease 正常
- DB 連線正常

再讓 Telegram webhook 指向：

```text
https://<PUBLIC_HOST>/api/telegram/webhook
```

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
- 大多數節點都可以直接設：
  - `AppRuntime__Profile=Standard`
- 只有真的要做特殊拆分時，才再額外覆寫細部 role 開關
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

### 6. 跑驗證腳本

本機 loopback 驗證：

```bash
bash deploy/ubuntu/check-runtime.sh
bash deploy/ubuntu/check-leases.sh
bash deploy/ubuntu/check-webhook.sh
```

如果你要直接打公開網址：

```bash
bash deploy/ubuntu/check-runtime.sh https://bot.example.com
bash deploy/ubuntu/check-leases.sh https://bot.example.com
bash deploy/ubuntu/check-webhook.sh https://bot.example.com
```

`check-webhook.sh` 會先看本機 `/api/telegram/health`，再用 env 檔裡的 bot token 去呼叫 Telegram `getWebhookInfo`，確認：

- bot 是否啟用
- webhook mode 是否開啟
- Telegram 端實際 webhook URL 是什麼
- pending update 數量
- 最近 webhook 錯誤

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

### 常用 profile

- `Standard`
  - 推薦給單機與多節點標準部署
  - 會開 `Webhook ingress + Update queue worker + scheduled jobs eligible`
- `WorkerOnly`
  - 不接 webhook，只吃 queue 與排程工作
- `IngressOnly`
  - 只接 webhook，不執行 queue 與排程
- `PollingNode`
  - 用在 polling 模式
- `Custom`
  - 完全手動控制細部 role

### 快速驗證部署是否正常

```bash
curl http://127.0.0.1:5050/api/runtime
curl http://127.0.0.1:5050/api/runtime/leases
curl http://127.0.0.1:5050/api/telegram/health
```

---

## 目前這版部署包的定位

這不是完整的平台化部署系統，也不是 K8s 替代品。

這包的定位比較像：

- 讓 side project 可以在 Ubuntu VM 上穩定跑
- 讓多節點 runtime role 與 leadership lease 能實際驗證
- 讓 demo、面試與日常維護都比較順手
