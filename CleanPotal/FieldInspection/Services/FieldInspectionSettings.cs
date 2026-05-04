using System;
using System.IO;
using System.Text.Json;

namespace CleanPotal.FieldInspection.Services
{
    /// <summary>
    /// 현장 점검 전역 설정.
    /// AppPaths.DataRoot/field_inspection_settings.json 에 저장.
    /// - BaseUrl: NFC/QR 가 가리킬 사내 모바일 웹 서버 주소
    /// - ServerSecret: 토큰(HMAC) 생성용 비밀키. 분실 시 모든 NFC/QR 재발급 필요.
    /// </summary>
    public class FieldInspectionSettings
    {
        public string BaseUrl { get; set; } = "http://10.10.40.98:5000";
        public string ServerSecret { get; set; } = "";

        private static readonly string ConfigPath =
            Path.Combine(AppPaths.DataRoot, "field_inspection_settings.json");

        private static FieldInspectionSettings? _cached;

        public static FieldInspectionSettings Current
        {
            get
            {
                if (_cached != null) return _cached;
                _cached = Load();
                return _cached;
            }
        }

        public static FieldInspectionSettings Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var loaded = JsonSerializer.Deserialize<FieldInspectionSettings>(json);
                    if (loaded != null)
                    {
                        if (string.IsNullOrEmpty(loaded.ServerSecret))
                            loaded.ServerSecret = GenerateSecret();
                        return loaded;
                    }
                }
            }
            catch { }

            var fresh = new FieldInspectionSettings { ServerSecret = GenerateSecret() };
            try { Save(fresh); } catch { }
            return fresh;
        }

        public static void Save(FieldInspectionSettings settings)
        {
            try
            {
                if (!Directory.Exists(AppPaths.DataRoot))
                    Directory.CreateDirectory(AppPaths.DataRoot);

                var json = JsonSerializer.Serialize(settings,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
                _cached = settings;
            }
            catch { }
        }

        private static string GenerateSecret()
        {
            var bytes = new byte[32];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }
    }
}
