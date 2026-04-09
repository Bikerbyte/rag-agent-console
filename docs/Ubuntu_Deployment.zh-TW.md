# Ubuntu Deployment

目前主線部署方式：

- `Local 主機`：PostgreSQL
- `VM1`：Docker app + Nginx + Cloudflare Tunnel
- `VM2`：Docker app

流量路徑：

`Telegram / Browser -> Tunnel 公開網址 -> VM1 Nginx -> VM1 app / VM2 app`

---

## Deploy 目錄

- `deploy/docker/vm1-polling.env.example`
- `deploy/docker/vm1-webhook.env.example`
- `deploy/docker/vm2-worker.env.example`
- `deploy/docker/check-runtime.sh`
- `deploy/docker/check-leases.sh`
- `deploy/docker/check-webhook.sh`
- `deploy/nginx/cpbl-telegram-assistant.conf`

---

## 1. Local PostgreSQL

至少確認：

- `listen_addresses = '*'`
- `pg_hba.conf` 有放行 VM 網段
- 防火牆有開 `5432`

範例：

```conf
host    all             all             192.168.0.0/24          scram-sha-256
```

從 VM 測：

```bash
nc -vz 192.168.0.223 5432
```

---

## 2. VM1

### 角色

- 初期可用 `PollingNode`
- 切 webhook 後改成 `Standard`

### Build image

```bash
docker build -t cpbl-telegram-assistant:latest .
```

### Polling 版 env

把 [vm1-polling.env.example](e:/Biker/Code/CPBLLineBotCloud/deploy/docker/vm1-polling.env.example) 複製成 `/opt/cpbl/.env`。

### Webhook 版 env

把 [vm1-webhook.env.example](e:/Biker/Code/CPBLLineBotCloud/deploy/docker/vm1-webhook.env.example) 複製成 `/opt/cpbl/.env`。

### 啟動 container

VM1 的 `8080` 已被 Jenkins 佔用，所以綁 `8088`：

```bash
docker rm -f cpbl-telegram-assistant 2>/dev/null || true

docker run -d \
  --name cpbl-telegram-assistant \
  --restart unless-stopped \
  --env-file /opt/cpbl/.env \
  -p 127.0.0.1:8088:8080 \
  cpbl-telegram-assistant:latest
```

### 驗證

```bash
docker logs -n 100 cpbl-telegram-assistant
curl http://127.0.0.1:8088/api/runtime
```

---

## 3. VM2

### 角色

- 建議用 `WorkerOnly`

### env

把 [vm2-worker.env.example](e:/Biker/Code/CPBLLineBotCloud/deploy/docker/vm2-worker.env.example) 複製成 `/opt/cpbl/.env`。

### 啟動 container

```bash
docker rm -f cpbl-telegram-assistant 2>/dev/null || true

docker run -d \
  --name cpbl-telegram-assistant \
  --restart unless-stopped \
  --env-file /opt/cpbl/.env \
  -p 8080:8080 \
  cpbl-telegram-assistant:latest
```

### 如果 VM2 空間不夠

改走：

VM1：

```bash
docker save cpbl-telegram-assistant:latest -o cpbl-telegram-assistant.tar
scp cpbl-telegram-assistant.tar <user>@<VM2_IP>:~/
```

VM2：

```bash
docker load -i ~/cpbl-telegram-assistant.tar
```

### 驗證

```bash
docker logs -n 100 cpbl-telegram-assistant
curl http://127.0.0.1:8080/api/runtime
```

---

## 4. VM1 Nginx

範本：

- [cpbl-telegram-assistant.conf](e:/Biker/Code/CPBLLineBotCloud/deploy/nginx/cpbl-telegram-assistant.conf)

重點：

- `127.0.0.1:8088` -> VM1 app
- `VM2_PRIVATE_IP:8080` -> VM2 app

啟用：

```bash
sudo cp deploy/nginx/cpbl-telegram-assistant.conf /etc/nginx/sites-available/cpbl-telegram-assistant.conf
sudo ln -sf /etc/nginx/sites-available/cpbl-telegram-assistant.conf /etc/nginx/sites-enabled/cpbl-telegram-assistant.conf
sudo nginx -t
sudo systemctl restart nginx
```

驗證：

```bash
curl -I http://127.0.0.1
curl http://127.0.0.1/api/runtime/leases
```

看 header 是否會出現：

- `X-App-Instance: vm-1`
- `X-App-Instance: vm-2`

---

## 5. Cloudflare Tunnel

### 正式 route

- Hostname: `bot.your-domain.com`
- Service: `http://localhost:80`

### Quick tunnel

如果沒有正式網域，可先測：

```bash
docker rm -f cloudflared 2>/dev/null || true
docker run --name cloudflared cloudflare/cloudflared:latest tunnel --url http://<VM1_LAN_IP>:80
```

注意：

- Docker 版 quick tunnel 不要直接用 `http://localhost:80`
- 這裡的 `localhost` 會指向 `cloudflared` container 自己，不是 VM 主機

先查 VM1 LAN IP：

```bash
hostname -I
```

### 驗證

```bash
docker logs -n 100 cloudflared
curl -I https://<your-public-host>
```

---

## 6. 切到 Webhook

當以下都正常時再切：

- VM1 app 正常
- VM2 app 正常
- Nginx upstream 正常
- Tunnel 正常

做法：

1. VM1 改用 [vm1-webhook.env.example](e:/Biker/Code/CPBLLineBotCloud/deploy/docker/vm1-webhook.env.example)
2. 把 `TelegramBot__WebhookUrl` 換成實際公開網址
3. 重啟 VM1 container

```bash
docker restart cpbl-telegram-assistant
docker logs -n 100 cpbl-telegram-assistant
```

驗證：

```bash
curl https://<your-public-host>/api/telegram/health
curl https://<your-public-host>/api/runtime
curl https://<your-public-host>/api/runtime/leases
```

---

## 7. 檢查腳本

VM1 經過 Nginx：

```bash
bash deploy/docker/check-runtime.sh
bash deploy/docker/check-leases.sh
bash deploy/docker/check-webhook.sh
```

VM2 直接打 app：

```bash
bash deploy/docker/check-runtime.sh http://127.0.0.1:8080
bash deploy/docker/check-leases.sh http://127.0.0.1:8080
```
