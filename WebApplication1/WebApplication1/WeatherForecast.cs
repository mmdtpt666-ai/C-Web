namespace WebApplication1
{
    /// <summary>原始 Web API 模板保留的天气预报响应，与视频业务无关。</summary>
    public class WeatherForecast
    {
        /// <summary>预报对应的本地日期。</summary>
        public DateOnly Date { get; set; }

        /// <summary>摄氏温度，单位为摄氏度。</summary>
        public int TemperatureC { get; set; }

        /// <summary>由摄氏温度换算并取整的华氏温度。</summary>
        public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);

        /// <summary>天气描述，允许为空。</summary>
        public string? Summary { get; set; }
    }
}
