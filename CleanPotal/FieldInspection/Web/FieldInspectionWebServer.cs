using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CleanPotal.FieldInspection.Models;
using CleanPotal.FieldInspection.Repositories;

namespace CleanPotal.FieldInspection.Web
{
    /// <summary>
    /// 사내망에서 휴대폰 브라우저로 접속하는 "데일리 체크리스트" 페이지를 서빙하는 경량 내장 웹서버.
    /// HttpListener 기반이라 추가 패키지 없이 동작하며, 추후 ASP.NET Core 등으로 이식 시
    /// 이 클래스의 라우팅/응답 로직만 옮기면 되도록 데이터 접근은 FieldInspectionRepository만 사용한다.
    ///
    /// 주의: 0.0.0.0(모든 IP)으로 열려면 관리자 권한이 필요하거나 사전에
    ///   netsh http add urlacl url=http://+:{port}/ user=Everyone
    /// 명령으로 URL 예약을 해두어야 한다. (HttpListener의 OS 제약)
    /// </summary>
    public sealed class FieldInspectionWebServer : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private readonly string _wwwRoot;
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;

        public int Port { get; }
        public bool IsRunning => _listener?.IsListening == true;

        private bool _isLanAccessible;

        public FieldInspectionWebServer(int port = 5180)
        {
            Port = port;
            _wwwRoot = Path.Combine(AppContext.BaseDirectory, "FieldInspection", "Web", "wwwroot");
        }

        public void Start()
        {
            if (IsRunning) return;

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{Port}/");
            try
            {
                _listener.Start();
                _isLanAccessible = true;
            }
            catch (HttpListenerException)
            {
                // 모든 IP 바인딩 권한이 없는 환경 — localhost로라도 동작하도록 폴백
                // (이 경우 휴대폰 등 외부 기기에서는 절대 접속할 수 없음 — URL 예약 또는 관리자 권한 필요)
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{Port}/");
                _listener.Start();
                _isLanAccessible = false;
            }

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;
            _cts = null;
            _isLanAccessible = false;
        }

        public void Dispose() => Stop();

        /// <summary>true면 사내망의 다른 기기(휴대폰 등)에서 접속 가능. false면 이 PC에서만 접속 가능 (권한 부족으로 폴백됨).</summary>
        public bool IsLanAccessible => _isLanAccessible;

        /// <summary>이 PC의 사내망 IP를 기준으로 휴대폰에서 접속할 주소를 만들어 반환.</summary>
        public string GetAccessUrl()
        {
            string ip = _isLanAccessible ? (GetLocalIPv4() ?? "localhost") : "localhost";
            return $"http://{ip}:{Port}/";
        }

        private static string? GetLocalIPv4()
        {
            try
            {
                return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                                && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString())
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        // -----------------------------------------------------------------------
        // 요청 처리 루프
        // -----------------------------------------------------------------------
        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (_listener != null && _listener.IsListening && !token.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { break; }

                _ = Task.Run(() => HandleRequestSafe(ctx), token);
            }
        }

        private async Task HandleRequestSafe(HttpListenerContext ctx)
        {
            try { await HandleRequestAsync(ctx); }
            catch (Exception ex)
            {
                try
                {
                    ctx.Response.StatusCode = 500;
                    await WriteJsonAsync(ctx.Response, new { error = ex.Message });
                }
                catch { }
            }
            finally
            {
                try { ctx.Response.OutputStream.Close(); } catch { }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var path = req.Url?.AbsolutePath ?? "/";
            var method = req.HttpMethod;

            // ---- API ----
            if (path.Equals("/api/checklists", StringComparison.OrdinalIgnoreCase) && method == "GET")
            {
                await HandleGetChecklistsAsync(ctx);
                return;
            }

            var todayMatch = MatchSegments(path, "api", "checklist", "*", "today");
            if (todayMatch != null && method == "GET")
            {
                await HandleGetTodayAsync(ctx, long.Parse(todayMatch[0]));
                return;
            }

            var submitMatch = MatchSegments(path, "api", "checklist", "*", "submit");
            if (submitMatch != null && method == "POST")
            {
                await HandlePostSubmitAsync(ctx, long.Parse(submitMatch[0]));
                return;
            }

            // ---- 정적 파일 ----
            await ServeStaticAsync(ctx, path);
        }

        /// <summary>경로가 prefix/.../*/.../suffix 형태인지 검사하고, '*' 위치의 값을 반환.</summary>
        private static string[]? MatchSegments(string path, params string[] pattern)
        {
            var segs = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length != pattern.Length) return null;

            var captured = new List<string>();
            for (int i = 0; i < pattern.Length; i++)
            {
                if (pattern[i] == "*") captured.Add(segs[i]);
                else if (!string.Equals(pattern[i], segs[i], StringComparison.OrdinalIgnoreCase)) return null;
            }
            return captured.ToArray();
        }

        // -----------------------------------------------------------------------
        // API: 활성 체크시트 목록
        // -----------------------------------------------------------------------
        private async Task HandleGetChecklistsAsync(HttpListenerContext ctx)
        {
            var locations = FieldInspectionRepository.GetLocations().ToDictionary(l => l.LocationId, l => l.Name);
            var list = FieldInspectionRepository.GetChecklists(onlyActive: true)
                .Select(c => new
                {
                    checklistId = c.ChecklistId,
                    code = c.Code,
                    category = c.Category,
                    name = c.Name,
                    cycle = c.Cycle,
                    locationId = c.LocationId,
                    locationName = c.LocationId.HasValue && locations.TryGetValue(c.LocationId.Value, out var n) ? n : ""
                })
                .ToList();

            await WriteJsonAsync(ctx.Response, list);
        }

        // -----------------------------------------------------------------------
        // API: 오늘자 체크리스트 (섹션별 그룹 + 기존 기록)
        // -----------------------------------------------------------------------
        private async Task HandleGetTodayAsync(HttpListenerContext ctx, long checklistId)
        {
            var q = ParseQuery(ctx.Request.Url?.Query);
            var checkDate = ParseDateOrToday(q.GetValueOrDefault("date"));
            var shift = q.GetValueOrDefault("shift") ?? "";

            var checklist = FieldInspectionRepository.GetChecklists().FirstOrDefault(c => c.ChecklistId == checklistId);
            if (checklist == null)
            {
                ctx.Response.StatusCode = 404;
                await WriteJsonAsync(ctx.Response, new { error = "체크시트를 찾을 수 없습니다." });
                return;
            }

            var allItems = FieldInspectionRepository.GetChecklistItems(checklistId);
            // 교대가 지정된 항목은 같은 교대만, 교대 구분이 없는 항목은 항상 포함
            var items = allItems.Where(i => string.IsNullOrEmpty(i.ShiftLabel) || string.Equals(i.ShiftLabel, shift, StringComparison.Ordinal))
                                .OrderBy(i => i.OrderNo)
                                .ToList();

            FieldInspectionRecord? record = null;
            Dictionary<long, FieldInspectionRecordItem> resultMap = new();
            if (checklist.LocationId.HasValue)
            {
                record = FieldInspectionRepository.GetRecordForDay(checklistId, checklist.LocationId.Value, checkDate, shift);
                if (record != null)
                {
                    foreach (var ri in FieldInspectionRepository.GetRecordItems(record.RecordId))
                        resultMap[ri.ItemId] = ri;
                }
            }

            var sections = items
                .GroupBy(i => string.IsNullOrWhiteSpace(i.SectionName) ? "" : i.SectionName)
                .Select(g => new
                {
                    sectionName = g.Key,
                    items = g.Select(i => new
                    {
                        itemId = i.ItemId,
                        orderNo = i.OrderNo,
                        title = i.Title,
                        inputType = i.InputType,
                        shiftLabel = i.ShiftLabel,
                        unitOrHint = i.UnitOrHint,
                        isRequired = i.IsRequired,
                        result = resultMap.TryGetValue(i.ItemId, out var r)
                            ? new { resultText = r.ResultText, isAbnormal = r.IsAbnormal, comment = r.Comment }
                            : null
                    })
                })
                .ToList();

            var payload = new
            {
                checklist = new { checklistId = checklist.ChecklistId, code = checklist.Code, category = checklist.Category, name = checklist.Name, cycle = checklist.Cycle },
                checkDate = checkDate.ToString("yyyy-MM-dd"),
                shiftLabel = shift,
                sections,
                record = record == null ? null : new
                {
                    recordId = record.RecordId,
                    inspectorName = record.InspectorName,
                    completedAt = record.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                    overallStatus = record.OverallStatus,
                    note = record.Note
                }
            };

            await WriteJsonAsync(ctx.Response, payload);
        }

        // -----------------------------------------------------------------------
        // API: 제출 (생성 또는 갱신)
        // -----------------------------------------------------------------------
        private async Task HandlePostSubmitAsync(HttpListenerContext ctx, long checklistId)
        {
            using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            var dto = JsonSerializer.Deserialize<SubmitDto>(body, JsonOpts);
            if (dto == null)
            {
                ctx.Response.StatusCode = 400;
                await WriteJsonAsync(ctx.Response, new { error = "잘못된 요청입니다." });
                return;
            }

            var checklist = FieldInspectionRepository.GetChecklists().FirstOrDefault(c => c.ChecklistId == checklistId);
            if (checklist?.LocationId == null)
            {
                ctx.Response.StatusCode = 400;
                await WriteJsonAsync(ctx.Response, new { error = "체크시트에 위치가 지정되어 있지 않습니다." });
                return;
            }

            var checkDate = ParseDateOrToday(dto.CheckDate);
            var shift = dto.ShiftLabel ?? "";
            var inspector = string.IsNullOrWhiteSpace(dto.InspectorName) ? "미상" : dto.InspectorName.Trim();

            bool anyAbnormal = dto.Items?.Any(i => i.IsAbnormal) == true;
            bool allFilled = dto.Items?.All(i => !string.IsNullOrWhiteSpace(i.ResultText)) == true;
            string overallStatus = anyAbnormal ? FieldInspectionConstants.StatusAbnormal
                                 : allFilled ? FieldInspectionConstants.StatusNormal
                                 : FieldInspectionConstants.StatusInProgress;

            // 같은 날 동일 체크시트 기록이 있으면 그대로 갱신(재제출)되도록 같은 RecordId 사용
            var existing = FieldInspectionRepository.GetRecordForDay(checklistId, checklist.LocationId.Value, checkDate, shift);

            var record = new FieldInspectionRecord
            {
                RecordId = existing?.RecordId ?? Guid.NewGuid().ToString("N"),
                LocationId = checklist.LocationId.Value,
                ChecklistId = checklistId,
                CheckDate = checkDate,
                ShiftLabel = shift,
                InspectorName = inspector,
                StartedAt = existing?.StartedAt ?? DateTime.Now,
                CompletedAt = DateTime.Now,
                OverallStatus = overallStatus,
                Note = dto.Note ?? "",
                ClientIp = ctx.Request.RemoteEndPoint?.Address?.ToString() ?? ""
            };
            record.Items = (dto.Items ?? new List<SubmitItemDto>()).Select(i => new FieldInspectionRecordItem
            {
                ItemId = i.ItemId,
                ResultText = i.ResultText ?? "",
                IsAbnormal = i.IsAbnormal,
                Comment = i.Comment ?? ""
            }).ToList();

            if (existing != null)
                FieldInspectionRepository.DeleteRecord(existing.RecordId);
            FieldInspectionRepository.InsertRecord(record);

            await WriteJsonAsync(ctx.Response, new { ok = true, recordId = record.RecordId, overallStatus = record.OverallStatus });
        }

        private sealed class SubmitDto
        {
            public string? CheckDate { get; set; }
            public string? ShiftLabel { get; set; }
            public string? InspectorName { get; set; }
            public string? Note { get; set; }
            public List<SubmitItemDto>? Items { get; set; }
        }

        private sealed class SubmitItemDto
        {
            public long ItemId { get; set; }
            public string? ResultText { get; set; }
            public bool IsAbnormal { get; set; }
            public string? Comment { get; set; }
        }

        // -----------------------------------------------------------------------
        // 정적 파일 서빙
        // -----------------------------------------------------------------------
        private async Task ServeStaticAsync(HttpListenerContext ctx, string path)
        {
            string rel = path == "/" ? "daily.html" : path.TrimStart('/');
            string full = Path.GetFullPath(Path.Combine(_wwwRoot, rel));

            // 경로 탈출 방지
            if (!full.StartsWith(Path.GetFullPath(_wwwRoot), StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            {
                ctx.Response.StatusCode = 404;
                byte[] nf = Encoding.UTF8.GetBytes("Not Found");
                await ctx.Response.OutputStream.WriteAsync(nf);
                return;
            }

            ctx.Response.ContentType = GetContentType(full);
            var bytes = await File.ReadAllBytesAsync(full);
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
        }

        private static string GetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };

        // -----------------------------------------------------------------------
        // 헬퍼
        // -----------------------------------------------------------------------
        private static Dictionary<string, string> ParseQuery(string? query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return result;
            foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                var key = Uri.UnescapeDataString(kv[0]);
                var val = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
                result[key] = val;
            }
            return result;
        }

        private static DateTime ParseDateOrToday(string? s)
            => DateTime.TryParse(s, out var dt) ? dt.Date : DateTime.Today;

        private static async Task WriteJsonAsync(HttpListenerResponse resp, object data)
        {
            resp.ContentType = "application/json; charset=utf-8";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(data, JsonOpts);
            resp.ContentLength64 = bytes.Length;
            await resp.OutputStream.WriteAsync(bytes);
        }
    }
}
