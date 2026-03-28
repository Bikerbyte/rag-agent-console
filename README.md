# CPBL Telegram Assistant

這是一個 CPBL 中華職棒 Telegram bot，目前提供以下功能:
- 查今天賽程
- 推今天最值得看的比賽
- 查某支球隊的近況
- 追蹤球隊，bot 會提供新聞或戰況
- 查詢最近的中職新聞
- 賽後用簡短文字整理重點

主要指令如下：
- `/today`
- `/today_best`
- `/team 兄弟`
- `/follow 兄弟`
- `/unfollow`
- `/my_follow`
- `/recap`
- `/news`

除了指令以外，也支援部分口語詢問，例如：
- `今天有什麼比賽`
- `統一近況`
- `味全今天有沒有打`
- `最新新聞`


## 本機啟動

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet"
dotnet run
```

## 資料庫

為 PostgreSQL：
```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=cpbl_telegram_assistant;Username=postgres;Password=your-password"
```

## Telegram 設定

先把 bot token 放進 user-secrets：
```powershell
dotnet user-secrets set "TelegramBot:Enabled" "true"
dotnet user-secrets set "TelegramBot:BotToken" "your-bot-token"
```