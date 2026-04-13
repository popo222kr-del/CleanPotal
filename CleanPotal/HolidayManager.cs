using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

namespace CleanPotal
{
    public static class HolidayManager
    {
        public class HolidayInfo
        {
            public string Date { get; set; } = "";
            public string Name { get; set; } = "";
        }

        // 대표님께서 발급받으신 공공데이터포털 일반 인증키
        private static readonly string ApiKey = "f949e0a7dee45d24f916e188162a633a275c387204d6e9d544cbf80ec490e589";

        public static async Task<List<string>> GetHolidaysAsync(int year)
        {
            // 파일 서버(NAS) 경로에 연도별 휴일 JSON 파일 지정
            string filePath = Path.Combine(AppPaths.DataRoot, $"holidays_{year}.json");

            // 1. 스마트 캐싱: 이미 파일 서버에 그 해의 공휴일이 저장되어 있다면 API를 호출하지 않고 0.1초 만에 불러옴
            if (File.Exists(filePath))
            {
                try
                {
                    string cachedJson = await File.ReadAllTextAsync(filePath);
                    return JsonSerializer.Deserialize<List<string>>(cachedJson) ?? new List<string>();
                }
                catch { /* 캐시 파일 오류 시 아래 API 호출로 넘어감 */ }
            }

            // 2. 파일이 없다면 정부 서버(API)에 최초 1회 접속하여 데이터 수집
            var holidays = new List<string>();
            try
            {
                using var client = new HttpClient();
                // 한국천문연구원 특일 정보 API URL 구성
                string url = $"http://apis.data.go.kr/B090041/openapi/service/SpcdeInfoService/getRestDeInfo?serviceKey={ApiKey}&solYear={year}&numOfRows=100&_type=json";

                string response = await client.GetStringAsync(url);

                using var doc = JsonDocument.Parse(response);
                var body = doc.RootElement.GetProperty("response").GetProperty("body");

                if (body.GetProperty("totalCount").GetInt32() > 0)
                {
                    var items = body.GetProperty("items").GetProperty("item");

                    // 휴일이 여러 개면 배열, 1개면 객체로 내려오는 한국 API 특성 예외 처리
                    if (items.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            string locdate = item.GetProperty("locdate").ToString(); // 예: "20261003"
                            holidays.Add($"{locdate.Substring(0, 4)}-{locdate.Substring(4, 2)}-{locdate.Substring(6, 2)}");
                        }
                    }
                    else if (items.ValueKind == JsonValueKind.Object)
                    {
                        string locdate = items.GetProperty("locdate").ToString();
                        holidays.Add($"{locdate.Substring(0, 4)}-{locdate.Substring(4, 2)}-{locdate.Substring(6, 2)}");
                    }
                }

                // 3. 수집한 공휴일 정보를 파일 서버에 자동 저장 (다음 사람을 위해)
                if (holidays.Count > 0)
                {
                    string jsonToSave = JsonSerializer.Serialize(holidays, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(filePath, jsonToSave);
                }
            }
            catch (Exception)
            {
                // 인터넷 연결 실패나 키 오류 시 로직 중단 없이 빈 리스트 반환 (주말은 여전히 걸러짐)
            }

            return holidays;
        }

        public static async Task<Dictionary<string, string>> GetHolidayMapAsync(int year)
        {
            string filePath = Path.Combine(AppPaths.DataRoot, $"holidays_detail_{year}.json");

            if (File.Exists(filePath))
            {
                try
                {
                    string cachedJson = await File.ReadAllTextAsync(filePath);
                    var cached = JsonSerializer.Deserialize<List<HolidayInfo>>(cachedJson) ?? new List<HolidayInfo>();
                    return cached
                        .Where(h => !string.IsNullOrWhiteSpace(h.Date))
                        .GroupBy(h => h.Date)
                        .ToDictionary(g => g.Key, g => g.First().Name);
                }
                catch { }
            }

            var holidayMap = new Dictionary<string, string>();
            try
            {
                using var client = new HttpClient();
                string url = $"http://apis.data.go.kr/B090041/openapi/service/SpcdeInfoService/getRestDeInfo?serviceKey={ApiKey}&solYear={year}&numOfRows=100&_type=json";
                string response = await client.GetStringAsync(url);

                using var doc = JsonDocument.Parse(response);
                var body = doc.RootElement.GetProperty("response").GetProperty("body");
                if (body.GetProperty("totalCount").GetInt32() > 0)
                {
                    var items = body.GetProperty("items").GetProperty("item");
                    if (items.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            string locdate = item.GetProperty("locdate").ToString();
                            string dateName = item.GetProperty("dateName").GetString() ?? "공휴일";
                            string normalized = $"{locdate.Substring(0, 4)}-{locdate.Substring(4, 2)}-{locdate.Substring(6, 2)}";
                            holidayMap[normalized] = dateName;
                        }
                    }
                    else if (items.ValueKind == JsonValueKind.Object)
                    {
                        string locdate = items.GetProperty("locdate").ToString();
                        string dateName = items.GetProperty("dateName").GetString() ?? "공휴일";
                        string normalized = $"{locdate.Substring(0, 4)}-{locdate.Substring(4, 2)}-{locdate.Substring(6, 2)}";
                        holidayMap[normalized] = dateName;
                    }
                }

                if (holidayMap.Count > 0)
                {
                    var serializable = holidayMap
                        .OrderBy(kv => kv.Key)
                        .Select(kv => new HolidayInfo { Date = kv.Key, Name = kv.Value })
                        .ToList();
                    string jsonToSave = JsonSerializer.Serialize(serializable, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(filePath, jsonToSave);
                }
            }
            catch (Exception)
            {
            }

            return holidayMap;
        }

        public static async Task<Dictionary<string, string>> GetHolidayNameMapAsync(int fromYear, int toYear)
        {
            int start = Math.Min(fromYear, toYear);
            int end = Math.Max(fromYear, toYear);
            var merged = new Dictionary<string, string>();

            for (int year = start; year <= end; year++)
            {
                var yearMap = await GetHolidayMapAsync(year);
                foreach (var kv in yearMap)
                {
                    merged[kv.Key] = kv.Value;
                }
            }

            return merged;
        }
    }
}