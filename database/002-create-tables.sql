-- 用途：创建用户表和视频目录表，字段约束与 AppDb 模型保持一致。
-- 前置条件：已执行 001-create-database.sql；执行账号具有建表和创建索引权限。
-- 重复执行只跳过已有表，不升级其结构；表结构变更应提供独立迁移脚本。
USE [Yink];
GO
-- 任一语句失败时回滚本次建表事务，避免留下部分初始化结构。
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users (
        -- 用户主键，由 SQL Server 自增生成。
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        -- 用于展示的原始邮箱，登录检索使用规范化字段。
        Email nvarchar(254) NOT NULL,
        -- 去除首尾空白后转大写的邮箱，唯一索引约束并发注册。
        NormalizedEmail nvarchar(254) NOT NULL,
        -- 用户展示昵称，不承担唯一身份标识。
        Name nvarchar(40) NOT NULL,
        -- PasswordHasher 编码的带盐哈希，禁止写入明文密码。
        PasswordHash nvarchar(512) NOT NULL
    );
    CREATE UNIQUE INDEX IX_Users_NormalizedEmail ON dbo.Users(NormalizedEmail);
END;
IF OBJECT_ID(N'dbo.Videos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Videos (
        -- 固定视频主键，与演示种子数据一致，不使用自增。
        Id int NOT NULL CONSTRAINT PK_Videos PRIMARY KEY,
        -- 视频展示标题。
        Title nvarchar(200) NOT NULL,
        -- 创作者或频道展示名称。
        Channel nvarchar(80) NOT NULL,
        -- 前端筛选分类，同时建立查询索引。
        Category nvarchar(40) NOT NULL,
        -- 演示观看量文案，不作为数值统计字段。
        Views nvarchar(40) NOT NULL,
        -- 演示相对发布时间，不代表真实时间戳。
        Age nvarchar(40) NOT NULL,
        -- 卡片演示时长，不代表本地演示短片的实际时长。
        Duration nvarchar(20) NOT NULL,
        -- 本地封面文件名，不含扩展名和路径。
        Cover nvarchar(40) NOT NULL
    );
    CREATE INDEX IX_Videos_Category ON dbo.Videos(Category);
END;
COMMIT;
GO
