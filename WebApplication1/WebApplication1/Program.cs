using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

// 配置优先级：基础配置、本机配置、环境变量；敏感连接信息不进入源码。
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// 将 Cookie 密钥持久化，使应用重启后仍能识别未过期的会话。
builder.Services.AddDataProtection().PersistKeysToFileSystem(
    new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, ".local", "keys")));
builder.Services.AddDbContext<AppDb>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("SqlServer") ?? ""));
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
// 浏览器仅持有 HttpOnly 会话 Cookie；生产环境必须通过 HTTPS 发送。
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "cweb.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
// 按客户端 IP 限制认证请求，减少密码猜测；不信任未经配置的转发请求头。
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    // 返回统一的数据库故障提示，详细异常仅写入服务端日志。
    try { await next(); }
    catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException && context.Request.Path.StartsWithSegments("/api"))
    {
        app.Logger.LogError(ex, "Database request failed");
        if (!context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = 503;
            await context.Response.WriteAsJsonAsync(new { message = "数据库暂不可用，请检查 SQL Server 连接配置。" });
        }
    }
});
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.MapControllers();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    // Cookie 会被浏览器自动携带，所有写操作必须同时验证 CSRF 令牌。
    if (context.Request.Path.StartsWithSegments("/api") && HttpMethods.IsPost(context.Request.Method))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { message = "页面验证已过期，请刷新后重试。" });
            return;
        }
    }
    await next();
});

// 仅按显式配置初始化全新数据库；已有数据库的结构升级使用独立 SQL 脚本。
if (app.Configuration.GetValue<bool>("Database:Initialize"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    await db.Database.EnsureCreatedAsync();
    if (!await db.Videos.AnyAsync()) { db.Videos.AddRange(VideoCatalog.Items); await db.SaveChangesAsync(); }
}

// 获取当前身份的 CSRF 请求令牌；登录或退出后，客户端必须重新获取。
app.MapGet("/api/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken });
});
// 查询 Cookie 中的当前用户身份；匿名请求返回 401，不重定向到 HTML 页面。
app.MapGet("/api/auth/me", (HttpContext context) => Results.Ok(new
{
    name = context.User.Identity!.Name,
    email = context.User.FindFirstValue(ClaimTypes.Email)
})).RequireAuthorization();

// 注册账号：校验输入、规范化邮箱并保存带盐密码哈希，不自动建立登录态。
app.MapPost("/api/auth/register", async (Credentials input, AppDb db, IPasswordHasher<AppUser> hasher) =>
{
    var error = Validate(input, registering: true);
    if (error != null) return Results.BadRequest(new { message = error });
    var email = input.Email!.Trim().ToUpperInvariant();
    if (await db.Users.AnyAsync(user => user.NormalizedEmail == email))
        return Results.Conflict(new { message = "此邮箱已注册，请直接登录。" });
    var user = new AppUser { Email = input.Email.Trim(), NormalizedEmail = email, Name = input.Name!.Trim() };
    user.PasswordHash = hasher.HashPassword(user, input.Password!);
    db.Users.Add(user);
    try { await db.SaveChangesAsync(); }
    // 唯一索引是并发注册的最终约束，不能仅依赖写入前的邮箱查询。
    catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627)
    { return Results.Conflict(new { message = "此邮箱已注册，请直接登录。" }); }
    return Results.Ok(new { message = "注册成功，请使用新账号登录。" });
}).RequireRateLimiting("auth");

// 校验密码后签发限时会话；邮箱不存在和密码错误使用相同的失败提示。
app.MapPost("/api/auth/login", async (Credentials input, AppDb db, IPasswordHasher<AppUser> hasher, HttpContext context) =>
{
    var error = Validate(input, registering: false);
    if (error != null) return Results.BadRequest(new { message = error });
    var email = input.Email!.Trim().ToUpperInvariant();
    var user = await db.Users.SingleOrDefaultAsync(user => user.NormalizedEmail == email);
    // 未找到账号时也执行哈希校验，减少通过响应时间推断账号存在性的风险。
    var candidate = user ?? new AppUser { PasswordHash = DummyHash.Value };
    var result = hasher.VerifyHashedPassword(candidate, candidate.PasswordHash, input.Password!);
    if (user == null || result == PasswordVerificationResult.Failed)
        return Results.Json(new { message = "邮箱或密码不正确。" }, statusCode: 401);
    // 密码策略升级后，在成功登录时重新计算哈希，不要求用户重置密码。
    if (result == PasswordVerificationResult.SuccessRehashNeeded)
    { user.PasswordHash = hasher.HashPassword(user, input.Password!); await db.SaveChangesAsync(); }
    var identity = new ClaimsIdentity(new[] {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Name),
        new Claim(ClaimTypes.Email, user.Email)
    }, CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Ok(new { name = user.Name, email = user.Email });
}).RequireRateLimiting("auth");
// 删除当前浏览器的认证 Cookie；此操作需要登录且必须通过 CSRF 验证。
app.MapPost("/api/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync();
    return Results.Ok(new { message = "已退出登录。" });
}).RequireAuthorization();
// 登录后只读查询视频目录；演示内容的标题、观看量和时长不代表真实统计。
app.MapGet("/api/videos", async (AppDb db) => Results.Ok(await db.Videos.AsNoTracking().OrderBy(v => v.Id).ToListAsync())).RequireAuthorization();
// 检查数据库连通性，失败返回 503；此接口不代表所有表结构均已校验。
app.MapGet("/api/health", async (AppDb db) =>
{
    try { return await db.Database.CanConnectAsync() ? Results.Ok(new { database = "connected" }) : Results.StatusCode(503); }
    catch { return Results.StatusCode(503); }
});
app.MapFallback("/api/{**path}", () => Results.NotFound(new { message = "接口不存在。" }));
app.Run();

/// <summary>校验认证请求的格式及长度，不访问数据库或修改输入对象。</summary>
/// <param name="input">客户端提交的邮箱、明文密码及可选昵称。</param>
/// <param name="registering">是否执行注册校验；登录时允许验证历史密码。</param>
/// <returns>校验失败时返回可展示的提示，通过时返回 null。</returns>
static string? Validate(Credentials input, bool registering)
{
    if (string.IsNullOrWhiteSpace(input.Email) || input.Email.Length > 254 || !new EmailAddressAttribute().IsValid(input.Email.Trim()))
        return "请输入有效的邮箱地址。";
    if (string.IsNullOrEmpty(input.Password) || input.Password.Length > 128 || (registering && input.Password.Length < 10))
        return "密码需为 10–128 个字符。";
    if (registering && (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 40))
        return "昵称需为 1–40 个字符。";
    return null;
}

/// <summary>认证请求数据，仅在请求处理期间使用，禁止记录明文密码。</summary>
/// <param name="Email">用于注册或登录的邮箱，服务端会去除首尾空白。</param>
/// <param name="Password">用户提交的明文密码，仅用于哈希计算和校验。</param>
/// <param name="Name">注册昵称，登录时可以省略。</param>
record Credentials(string? Email, string? Password, string? Name);
/// <summary>映射 dbo.Users 的用户实体，不保存明文密码。</summary>
class AppUser
{
    /// <summary>数据库生成的用户主键。</summary>
    public int Id { get; set; }
    /// <summary>保留用户输入大小写的邮箱，用于身份展示。</summary>
    public string Email { get; set; } = "";
    /// <summary>去除首尾空白并按固定区域规则转大写的邮箱，受唯一索引约束。</summary>
    public string NormalizedEmail { get; set; } = "";
    /// <summary>最多 40 个字符的显示昵称。</summary>
    public string Name { get; set; } = "";
    /// <summary>PasswordHasher 生成的含算法信息、随机盐和派生密钥的编码结果。</summary>
    public string PasswordHash { get; set; } = "";
}
/// <summary>SQL Server 数据访问上下文，生命周期与单次请求作用域一致。</summary>
/// <param name="options">依赖注入提供的 SQL Server 连接和 EF Core 配置。</param>
class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    /// <summary>用户账号及密码哈希集合。</summary>
    public DbSet<AppUser> Users => Set<AppUser>();
    /// <summary>登录后可读取的视频目录集合。</summary>
    public DbSet<Video> Videos => Set<Video>();
    /// <summary>配置与 database/002-create-tables.sql 一致的字段长度和索引约束。</summary>
    /// <param name="model">EF Core 模型构建器。</param>
    protected override void OnModelCreating(ModelBuilder model)
    {
        var user = model.Entity<AppUser>();
        user.HasIndex(x => x.NormalizedEmail).IsUnique();
        user.Property(x => x.NormalizedEmail).HasMaxLength(254);
        user.Property(x => x.Email).HasMaxLength(254);
        user.Property(x => x.Name).HasMaxLength(40);
        user.Property(x => x.PasswordHash).HasMaxLength(512);
        var video = model.Entity<Video>();
        video.HasKey(x => x.Id);
        video.Property(x => x.Id).ValueGeneratedNever();
        video.Property(x => x.Title).HasMaxLength(200);
        video.Property(x => x.Channel).HasMaxLength(80);
        video.Property(x => x.Category).HasMaxLength(40);
        video.Property(x => x.Views).HasMaxLength(40);
        video.Property(x => x.Age).HasMaxLength(40);
        video.Property(x => x.Duration).HasMaxLength(20);
        video.Property(x => x.Cover).HasMaxLength(40);
        video.HasIndex(x => x.Category);
    }
}
/// <summary>为不存在的账号提供一次有效哈希校验输入，避免直接跳过密码计算。</summary>
static class DummyHash
{
    /// <summary>进程内生成的随机密码哈希，不对应任何可登录账号。</summary>
    public static readonly string Value = new PasswordHasher<AppUser>().HashPassword(new AppUser(), Guid.NewGuid().ToString());
}
