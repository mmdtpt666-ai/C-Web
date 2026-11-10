# 映刻 YINK

基于原有 ASP.NET Core 8 Web API 的视频站点示例。Vue 3.5.22 通过本地 `<script>` 加载，无需 Node.js、npm 或前端构建。SQL Server 保存用户与视频目录。

## 功能

- 邮箱注册、登录、登录状态恢复、退出。密码由 ASP.NET PasswordHasher 使用带随机盐的 PBKDF2 哈希保存。
- HttpOnly / SameSite Cookie 认证、CSRF 验证、注册和登录限流、邮箱唯一索引。
- 登录后的推荐视频界面、搜索、分类、播放器、稍后观看和观看记录，支持手机布局。
- 稍后观看和历史记录按账号保存在当前浏览器，不会在设备之间同步。
- 视频标题、观看量、时长是示例数据；所有卡片播放同一段本地演示短片，不代表实际对应视频。

## 本机启动（Docker + .NET 8）

需要 Docker、.NET 8 SDK、Python 3 和 Bash。云环境已经安装的 SDK 位于 `/workspace/tools/dotnet`，若不在 PATH 中：

```bash
export PATH="/workspace/tools/dotnet:$PATH"
export DOTNET_CLI_HOME=/workspace/tools/dotnet-home
export NUGET_PACKAGES=/workspace/tools/nuget
```

在仓库根目录运行：

```bash
bash scripts/start-local.sh
```

脚本拉取 Microsoft SQL Server 2022 Developer 镜像（接受开发版许可，仅用于开发），生成随机本机管理员密码，创建持久化 Docker 卷，等待数据库就绪，按顺序执行 `database/*.sql`，生成忽略提交的本地连接配置，并启动应用。SQL Server 仅绑定本机端口 `14333`，应用监听端口 `5080`。

浏览器打开应用根路径，先注册再登录，无预置账号或通用密码。Visual Studio 可直接打开 `WebApplication1/WebApplication1.sln`；配置完成后运行原有 http/https 启动项即可。

重复执行启动脚本不会重置用户或视频数据。SQL Server 数据保存在卷 `yink-sqlserver-data`，密码在 `.local/sqlserver.env`，连接配置在 `WebApplication1/WebApplication1/appsettings.Local.json`；均已加入忽略规则。已有容器的管理员密码必须与该文件一致，勿删除密码文件后继续复用原卷。应用 Cookie 密钥保存在项目 `.local/keys`，不要提交或公开此目录。

停止数据库：`docker stop yink-sqlserver`。脚本前台运行应用，Ctrl+C 停止应用，不删除数据库。

## SQL 脚本与手动连接

按以下顺序，在 SQL Server Management Studio 或 `sqlcmd` 执行：

1. `database/001-create-database.sql`：创建 `Yink` 数据库。
2. `database/002-create-tables.sql`：创建 `Users`、`Videos` 表及主键、唯一索引和分类索引。
3. `database/003-seed-videos.sql`：写入九条演示视频，已有相同 ID 的记录保留。

脚本支持重复执行，不包含账号密码，不会删除或清空已有表；已有表的结构变更需另写迁移脚本。

可通过环境变量 `ConnectionStrings__SqlServer` 或忽略提交的 `appsettings.Local.json` 配置连接。连接示例（替换占位符，不要提交真实密码）：

```json
{
  "ConnectionStrings": {
    "SqlServer": "Server=127.0.0.1,14333;Database=Yink;User Id=sa;Password=<本机生成的密码>;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
  }
}
```

`TrustServerCertificate=True` 只用于本机 SQL Server 自签名证书。部署时使用可信证书、`TrustServerCertificate=False`、专用最小权限数据库账号及 HTTPS；保管并持久化 Cookie 密钥。生产环境 Cookie 强制 Secure，请通过 HTTPS 访问。`Database:Initialize=true` 可用于全新开发数据库的 EF 初始化与演示数据导入，默认关闭；推荐使用保存的 SQL 脚本，不要用于维护已有数据库结构。

## 验证

```bash
dotnet restore WebApplication1/WebApplication1 --locked-mode
dotnet build WebApplication1/WebApplication1 --no-restore
python3 scripts/smoke-test.py
python3 scripts/smoke-test.py --database
```

基础检查验证页面、未登录访问控制、CSRF 和输入校验；`--database` 进一步验证真实注册、重复邮箱、错误密码、登录、视频目录和退出，每次创建一个随机测试账号。失败退出码非零。认证限流每 IP 每分钟 10 次，连续重复测试前等待窗口刷新。

开发环境保留 Swagger `/swagger` 及原有 WeatherForecast 示例接口。`GET /api/health` 检查数据库连接，数据库不可用返回 503；缺少连接配置不会生成假登录或自动跳过认证。

## 当前实例验证结果

网络权限更新后，已在云环境本机安装并启动 SQL Server 2022 Developer，执行三个 SQL 脚本，创建 `Yink` 数据库、`Users` / `Videos` 表及九条示例视频。数据库使用 Docker 卷持久化，连接配置已生成到忽略提交的本地文件中，没有把密码写入源码或 SQL 脚本。

已通过 `python3 scripts/smoke-test.py --database`：真实注册、重复邮箱拒绝、错误密码拒绝、登录、登录态查询、读取九条视频、退出及退出后访问控制。另用真实浏览器连接实际 API 和 SQL Server，验证注册、登录、刷新后登录态恢复、九张封面展示和退出；此次未模拟接口。集成测试会创建随机测试账号，不删除原有数据。

SQL Server 容器重启后，测试账号和九条视频均保留，应用健康检查再次返回 `database: connected`。

此前已通过 .NET 8 编译（0 警告、0 错误）、依赖锁定还原、基础 HTTP 检查，以及浏览器中的搜索、分类、稍后观看、历史记录、演示播放和手机布局检查。当前运行状态不代表已发布新环境版本；新实例仍需执行启动脚本恢复进程。

## 素材

- Vue 本地发行文件：Vue 3.5.22，MIT，源码版权声明保留于文件内。
- 视频封面为项目自带 SVG 插画，无外部图片服务依赖。
- `wwwroot/media/flower.mp4` 来自 [MDN interactive examples 的 CC0 视频](https://github.com/mdn/interactive-examples/tree/main/live-examples/media/cc0-videos)。

后续云任务复用当前 checkout，无需另建 Git worktree。

## 注释约定

参考阿里巴巴开发规约的注释原则：类和方法提供文档说明，参数、返回值、异常及边界条件与实现保持一致；关键分支解释业务原因，避免逐行翻译代码。C# 使用 XML 文档注释，JavaScript 使用 JSDoc，SQL 与启动脚本使用相应语言的行注释。第三方 Vue 发行文件及自动生成的依赖文件保留原样，不人工追加业务注释。
