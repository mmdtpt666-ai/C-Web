/// <summary>视频目录实体，统计字段为演示文案；所有卡片播放同一本地演示短片。</summary>
/// <param name="Id">与种子 SQL 一致的固定视频主键。</param>
/// <param name="Title">视频展示标题。</param>
/// <param name="Channel">创作者或频道展示名称。</param>
/// <param name="Category">用于前端筛选的分类名称。</param>
/// <param name="Views">演示观看量文案，不用于计数计算。</param>
/// <param name="Age">演示发布时间文案，不代表真实时间戳。</param>
/// <param name="Duration">演示时长文案，不代表演示短片的实际时长。</param>
/// <param name="Cover">本地 SVG 封面的文件名，不含扩展名和路径。</param>
record Video(int Id, string Title, string Channel, string Category, string Views, string Age, string Duration, string Cover);
/// <summary>全新开发数据库使用的演示目录，与 database/003-seed-videos.sql 保持一致。</summary>
static class VideoCatalog
{
    /// <summary>按固定主键排列的九条初始化数据，不在每次请求时重新写入数据库。</summary>
    public static readonly Video[] Items = [
        new(1, "把日子过慢一点：山野里的 48 小时", "山外来信", "生活", "12.8万次观看", "2天前", "12:48", "mountains"),
        new(2, "凌晨五点的咖啡馆，和一座刚醒来的城市", "日常放映室", "生活", "8.2万次观看", "1天前", "08:32", "coffee"),
        new(3, "从零搭建我的极简桌面｜工作也可以很舒服", "好好造物", "科技", "23.6万次观看", "3天前", "16:05", "desk"),
        new(4, "独自去海边，听风把烦恼带走", "漫游指南", "旅行", "6.4万次观看", "5小时前", "10:24", "sea"),
        new(5, "今天不赶路，在京都的小巷里迷路", "慢半拍旅行", "旅行", "18.9万次观看", "4天前", "18:36", "city"),
        new(6, "给周末的一份礼物：手作意式番茄面", "一人食记", "美食", "9.7万次观看", "2天前", "06:58", "food"),
        new(7, "适合专注时循环的温柔旋律", "Soft Sounds", "音乐", "42.1万次观看", "1周前", "32:10", "music"),
        new(8, "用一个下午，理解光与影的关系", "视觉笔记", "学习", "5.3万次观看", "3天前", "14:22", "art"),
        new(9, "走进森林，给生活按一下暂停键", "山外来信", "生活", "11.2万次观看", "6天前", "09:41", "forest")
    ];
}
