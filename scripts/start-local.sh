#!/usr/bin/env bash
# 用途：启动本机开发 SQL Server，执行幂等 SQL 脚本并运行 Web API。
# 前置条件：Docker、.NET 8 SDK、Python 3、Bash 可用；仅用于开发环境。
# 密码和连接配置写入 Git 忽略目录，重复执行复用已有容器和数据卷。
set -euo pipefail
cd "$(dirname "$0")/.."
command -v docker >/dev/null || { echo 'Docker is required.' >&2; exit 1; }
command -v dotnet >/dev/null || { echo '.NET 8 SDK is required.' >&2; exit 1; }
# 新建凭据文件仅允许当前用户读取，禁止通过日志输出密码。
umask 077
mkdir -p .local
if [ ! -f .local/sqlserver.env ]; then
  python3 - <<'PY'
import secrets
from pathlib import Path
Path('.local/sqlserver.env').write_text('ACCEPT_EULA=Y\nMSSQL_PID=Developer\nMSSQL_SA_PASSWORD=Yink!'+secrets.token_hex(24)+'\n')
PY
fi
if ! docker container inspect yink-sqlserver >/dev/null 2>&1; then
  docker run --name yink-sqlserver --env-file .local/sqlserver.env \
    -p 127.0.0.1:14333:1433 --memory=3g --cpus=2 \
    -v yink-sqlserver-data:/var/opt/mssql -d mcr.microsoft.com/mssql/server:2022-latest >/dev/null
else
  docker start yink-sqlserver >/dev/null
fi
# 数据库进程启动不代表可查询，必须等待一次真实 SQL 请求成功。
ready=false
for attempt in $(seq 1 60); do
  if docker exec yink-sqlserver bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -Q "SELECT 1" -b -l 2' >/dev/null 2>&1; then ready=true; break; fi
  sleep 1
done
if [ "$ready" != true ]; then echo 'SQL Server did not become ready. Inspect docker logs yink-sqlserver.' >&2; exit 1; fi
# 文件名前缀保证先建库、再建表、最后写入种子数据；任一步失败即停止。
for script in database/*.sql; do
  docker exec -i yink-sqlserver bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b' < "$script"
done
python3 - <<'PY'
import json
from pathlib import Path
password=dict(line.split('=',1) for line in Path('.local/sqlserver.env').read_text().splitlines())['MSSQL_SA_PASSWORD']
path=Path('WebApplication1/WebApplication1/appsettings.Local.json')
if not path.exists():
    path.write_text(json.dumps({'ConnectionStrings':{'SqlServer':f'Server=127.0.0.1,14333;Database=Yink;User Id=sa;Password={password};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5'}},indent=2))
PY
export ASPNETCORE_ENVIRONMENT=Development
dotnet run --project WebApplication1/WebApplication1 --no-launch-profile --urls http://127.0.0.1:5080
