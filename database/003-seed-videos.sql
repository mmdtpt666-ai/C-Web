-- 用途：向 Yink 写入九条演示目录；全部卡片播放同一本地 CC0 短片。
-- 前置条件：已执行 002-create-tables.sql；标题、观看量和时长均为演示数据。
-- 重复执行保留相同 ID 的已有记录，不覆盖用户调整后的目录。
USE [Yink];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
INSERT INTO dbo.Videos (Id, Title, Channel, Category, Views, Age, Duration, Cover)
SELECT source.* FROM (VALUES
(1, N'把日子过慢一点：山野里的 48 小时', N'山外来信', N'生活', N'12.8万次观看', N'2天前', N'12:48', N'mountains'),
(2, N'凌晨五点的咖啡馆，和一座刚醒来的城市', N'日常放映室', N'生活', N'8.2万次观看', N'1天前', N'08:32', N'coffee'),
(3, N'从零搭建我的极简桌面｜工作也可以很舒服', N'好好造物', N'科技', N'23.6万次观看', N'3天前', N'16:05', N'desk'),
(4, N'独自去海边，听风把烦恼带走', N'漫游指南', N'旅行', N'6.4万次观看', N'5小时前', N'10:24', N'sea'),
(5, N'今天不赶路，在京都的小巷里迷路', N'慢半拍旅行', N'旅行', N'18.9万次观看', N'4天前', N'18:36', N'city'),
(6, N'给周末的一份礼物：手作意式番茄面', N'一人食记', N'美食', N'9.7万次观看', N'2天前', N'06:58', N'food'),
(7, N'适合专注时循环的温柔旋律', N'Soft Sounds', N'音乐', N'42.1万次观看', N'1周前', N'32:10', N'music'),
(8, N'用一个下午，理解光与影的关系', N'视觉笔记', N'学习', N'5.3万次观看', N'3天前', N'14:22', N'art'),
(9, N'走进森林，给生活按一下暂停键', N'山外来信', N'生活', N'11.2万次观看', N'6天前', N'09:41', N'forest')
) AS source (Id, Title, Channel, Category, Views, Age, Duration, Cover)
-- 事务内锁定存在性检查，避免并发初始化时插入相同主键。
WHERE NOT EXISTS (SELECT 1 FROM dbo.Videos WITH (UPDLOCK, HOLDLOCK) WHERE Id = source.Id);
COMMIT;
GO
