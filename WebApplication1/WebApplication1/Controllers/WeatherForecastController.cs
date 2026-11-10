using Microsoft.AspNetCore.Mvc;

namespace WebApplication1.Controllers
{
    /// <summary>保留模板天气接口，返回随机演示数据，不读取真实气象服务。</summary>
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        // 随机天气描述的候选值，仅供模板接口演示。
        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        private readonly ILogger<WeatherForecastController> _logger;

        /// <summary>创建模板天气控制器。</summary>
        /// <param name="logger">框架注入的控制器日志记录器。</param>
        public WeatherForecastController(ILogger<WeatherForecastController> logger)
        {
            _logger = logger;
        }

        /// <summary>生成从明天开始的五天天气演示数据，每次调用重新随机生成。</summary>
        /// <returns>包含日期、温度及描述的五条预报。</returns>
        [HttpGet(Name = "GetWeatherForecast")]
        public IEnumerable<WeatherForecast> Get()
        {
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            })
            .ToArray();
        }
    }
}
