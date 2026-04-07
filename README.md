# CPBL Telegram Assistant

一個以 **ASP.NET Core** 開發的 Telegram Bot，主要用來整理與推送 **CPBL 中華職棒** 的比賽資訊。  
使用者可以直接在 Telegram 查詢今日賽程、即時比分、戰績排名、球隊近況，也可以追蹤喜歡的球隊，接收開賽、終場與新聞通知。

---

## 目前功能

- 查今天賽程、明天賽程
- 查即時比分與對戰狀態
- 查昨天賽果、今日完賽結果
- 查目前排名
- 查指定球隊的近況與下一場比賽
- 追蹤喜歡的球隊
- 接收開賽、終場、新聞等通知
- 查看最新中職新聞
- 以簡短文字整理當日比賽重點
- 透過 Razor Pages 後台管理訂閱、log、runtime node 與 leadership lease 狀態

---

## 主要指令

- `/today`：今天賽程
- `/tomorrow`：明天賽程
- `/yesterday`：昨天已完賽結果
- `/result`：今天已完賽結果
- `/standings`：目前排名
- `/follow 兄弟`：追蹤指定球隊
- `/following`：查看目前追蹤隊伍
- `/team 兄弟`：查看球隊近況摘要
- `/next 兄弟`：查看這支球隊下一場比賽
- `/notify`：查看提醒狀態
- `/notify game on`：開啟比賽提醒
- `/recap`：查看今日賽事重點整理
- `/news`：查看最新新聞

---

## 系統架構

目前這個專案維持 **same codebase, multi-node deployment by runtime roles** 的設計：

- 同一份 ASP.NET Core 程式可部署到單台或多台 VM
- `Webhook ingress` 可以多台同時開啟
- `Telegram update queue worker` 維持多節點協作 claim batch
- `scheduled jobs` 則改成 **lease-based single-active execution**

也就是說：

- `OfficialDataSyncBackgroundService`
- `TelegramNotificationBackgroundService`

這兩種排程型工作可以在多台節點上都具備執行資格，但同一時間只會有一台節點持有對應 lease 並真正執行；若持有者掛掉，租約過期後其他節點可自動接手。

### PostgreSQL 目前負責保存

- 業務資料：球隊、賽程、新聞、群組訂閱
- queue 狀態：`TelegramUpdateInbox`
- runtime node 心跳：`RuntimeNodeHeartbeats`
- leadership lease 狀態：`RuntimeLeadershipLeases`
- push / sync logs

### 後台目前可看到

- 線上 node 清單
- 各節點 runtime role
- leadership lease 目前持有者
- queue 收件與處理流向

多節點架構說明可參考 [docs/Scalable_Multi_Node_Architecture.zh-TW.md](docs/Scalable_Multi_Node_Architecture.zh-TW.md)。

---

## 技術組成

- **Backend**：ASP.NET Core
- **UI / Admin**：Razor Pages
- **Database**：PostgreSQL
- **Bot Platform**：Telegram Bot API
- **Deployment**：Docker / Ubuntu VM
- **Data Source**：CPBL 官方資料

---

## 環境設定

### 1. Clone 專案

```bash
git clone https://github.com/Bikerbyte/Baseball_Bot.git
cd Baseball_Bot
```

### 2. 設定資料庫連線

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=cpbl_telegram_assistant;Username=postgres;Password=your-password"
```

### 3. 設定 Telegram Bot

```powershell
dotnet user-secrets set "TelegramBot:Enabled" "true"
dotnet user-secrets set "TelegramBot:BotToken" "your-bot-token"
```

### 4. 套用 migration

```bash
dotnet ef database update
```

### 5. 執行專案

```bash
dotnet run
```

---

## 多節點部署備註

`AppRuntime` 目前同時負責：

- runtime role 開關
- leadership lease 設定
- node instance name

常用設定包含：

- `AppRuntime__InstanceName`
- `AppRuntime__EnableLeadershipLease`
- `AppRuntime__LeaseDurationSeconds`
- `AppRuntime__LeaseRenewIntervalSeconds`
- `AppRuntime__LeaseAcquireRetrySeconds`
- `AppRuntime__EnableTelegramWebhookIngress`
- `AppRuntime__EnableTelegramUpdateQueueWorker`
- `AppRuntime__EnableOfficialDataSyncWorker`
- `AppRuntime__EnableNotificationWorker`

實務上可以讓多台節點都開：

- `Webhook ingress`
- `Update queue worker`
- `Official sync worker`
- `Notification worker`

然後交由 leadership lease 決定哪台節點真正執行 scheduled jobs，而不是靠固定 primary node。

Ubuntu VM 的實際部署方式可參考 [docs/Ubuntu_Deployment.zh-TW.md](docs/Ubuntu_Deployment.zh-TW.md)。
