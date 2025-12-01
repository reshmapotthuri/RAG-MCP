
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;
using System.Globalization;

namespace Chat.Plugins
{
    public class GetWeather
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public GetWeather(IHttpClientFactory httpClientFactory) =>
            _httpClientFactory = httpClientFactory;

        [KernelFunction("get_weather_forecast")]
        [Description("Get the current weather for a given latitude and longitude in India Standard Time")]
        [return: Description("Returns formatted weather string including temperature, wind, and condition")]
        public async Task<string> GetWeatherPointAsync(decimal latitude, decimal longitude)
        {
            var client = _httpClientFactory.CreateClient();
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current_weather=true";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "myapplication-udemy-course");

            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var current = doc.RootElement.GetProperty("current_weather");

            string utcTime = current.GetProperty("time").GetString() ?? "";
            DateTime utcDateTime = DateTime.SpecifyKind(
     DateTime.Parse(utcTime, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
     DateTimeKind.Utc
 );
            TimeZoneInfo istZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            DateTime istTime = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, istZone);

            string temp = current.GetProperty("temperature").ToString();
            string windspeed = current.GetProperty("windspeed").ToString();
            string winddir = current.GetProperty("winddirection").ToString();
            string weathercode = current.GetProperty("weathercode").ToString();

            string condition = weathercode switch
            {
                "0" => "Clear sky",
                "1" => "Mainly clear",
                "2" => "Partly cloudy",
                "3" => "Overcast",
                "45" => "Fog",
                "48" => "Rime fog",
                "51" => "Light drizzle",
                "61" => "Light rain",
                "71" => "Light snow",
                _ => "Unknown condition"
            };

            return $"At {istTime:yyyy-MM-dd HH:mm} IST, weather: {temp}°C, {condition}, wind {windspeed} km/h from {winddir}°.";
        }
    }
}