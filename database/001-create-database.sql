-- 用途：在 master 上创建本机开发数据库；由具备建库权限的管理员执行。
-- 前置条件：SQL Server 已启动。重复执行保留已有数据库，不包含任何账号密码。
-- 执行顺序：001 建库 → 002 建表 → 003 写入演示目录。
USE [master];
GO
IF DB_ID(N'Yink') IS NULL
BEGIN
    CREATE DATABASE [Yink];
END;
GO
