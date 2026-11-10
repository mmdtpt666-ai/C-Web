#!/usr/bin/env python3
"""验证 HTTP 访问控制及可选的真实 SQL Server 认证流程。

默认仅运行不依赖数据库的检查；--database 会创建一个随机测试账号，
保留原有数据且不删除测试账号。任一断言失败时以非零状态退出。
"""
import argparse
import http.cookiejar
import json
import secrets
import urllib.error
import urllib.request

parser = argparse.ArgumentParser()
parser.add_argument('--url', default='http://127.0.0.1:5080')
parser.add_argument('--database', action='store_true')
args = parser.parse_args()
client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

def request(path, body=None, token=None, expected=200):
    """发送携带共享 Cookie 的请求，并校验预期 HTTP 状态。

    Args:
        path: 相对于应用地址的 API 或静态资源路径。
        body: JSON 请求体；为 None 时发送 GET，否则发送 POST。
        token: 当前身份的 CSRF 请求令牌，读取请求可以省略。
        expected: 预期 HTTP 状态码，默认 200。

    Returns:
        JSON 响应解析为 Python 对象，其他响应返回原始字节。

    Raises:
        AssertionError: 实际状态码不符合预期。
        urllib.error.URLError: 服务不可达或发生网络错误。
    """
    headers = {}
    if token:
        headers['X-CSRF-TOKEN'] = token
    if body is not None:
        headers['Content-Type'] = 'application/json'
    req = urllib.request.Request(args.url + path, data=json.dumps(body).encode() if body is not None else None, headers=headers)
    try:
        response = client.open(req)
    except urllib.error.HTTPError as error:
        response = error
    payload = response.read()
    assert response.code == expected, f'{path}: expected {expected}, got {response.code}: {payload!r}'
    print(f'PASS {req.get_method()} {path} -> {response.code}')
    return json.loads(payload) if response.headers.get_content_type() == 'application/json' else payload

assert b'vue.global.prod.js' in request('/')
request('/api/auth/me', expected=401)
request('/api/videos', expected=401)
request('/api/auth/register', {}, expected=400)  # 缺少 CSRF 令牌的写请求必须被拒绝。
token = request('/api/auth/csrf')['token']
request('/api/auth/register', {'email': 'invalid', 'password': 'short', 'name': ''}, token, 400)
request('/api/auth/login', {'email': 'invalid', 'password': ''}, token, 400)
if args.database:
    request('/api/health')
    email = f'smoke-{secrets.token_hex(6)}@example.test'
    credentials = {'email': email, 'password': secrets.token_urlsafe(24), 'name': '冒烟测试'}
    request('/api/auth/register', credentials, token)
    request('/api/auth/register', credentials, token, 409)
    request('/api/auth/login', {**credentials, 'password': 'wrong-password'}, token, 401)
    assert request('/api/auth/login', credentials, token)['name'] == credentials['name']
    assert request('/api/auth/me')['email'] == email
    assert len(request('/api/videos')) == 9
    token = request('/api/auth/csrf')['token']
    request('/api/auth/logout', {}, token)
    request('/api/auth/me', expected=401)
    request('/api/videos', expected=401)
    print('Database checks passed. One test account was created; no existing data was deleted.')
else:
    print('Database registration/login checks NOT RUN. Use --database after SQL Server initialization.')
