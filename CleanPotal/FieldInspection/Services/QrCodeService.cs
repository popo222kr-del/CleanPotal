using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using QRCoder;

namespace CleanPotal.FieldInspection.Services
{
    /// <summary>
    /// 태그 ID/토큰 발급 + URL 조립 + QR 이미지 생성.
    /// 모든 토큰은 HMAC-SHA256(TagId, ServerSecret) 의 Base64URL 16바이트 prefix.
    /// 같은 TagId 라도 ServerSecret 이 바뀌면 토큰이 무효화되도록 설계.
    /// </summary>
    public static class QrCodeService
    {
        public static string NewTagId() => "TAG-" + Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant();

        public static string ComputeToken(string tagId)
        {
            var settings = FieldInspectionSettings.Current;
            string secret = string.IsNullOrEmpty(settings.ServerSecret) ? "default-secret" : settings.ServerSecret;

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(tagId));

            // 짧고 URL 안전한 토큰 (16바이트 → 22자)
            return Convert.ToBase64String(hash, 0, 16)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        public static string BuildUrl(string tagId, string token)
        {
            var settings = FieldInspectionSettings.Current;
            string baseUrl = (settings.BaseUrl ?? "").TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) baseUrl = "http://localhost";
            return $"{baseUrl}/c/{tagId}?t={token}";
        }

        public static byte[] GeneratePngBytes(string payload, int pixelsPerModule = 8)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data);
            return png.GetGraphic(pixelsPerModule);
        }

        public static BitmapImage GenerateBitmap(string payload, int pixelsPerModule = 8)
        {
            byte[] bytes = GeneratePngBytes(payload, pixelsPerModule);
            var image = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();
            }
            return image;
        }

        public static void SavePng(string payload, string filePath, int pixelsPerModule = 12)
        {
            byte[] bytes = GeneratePngBytes(payload, pixelsPerModule);
            File.WriteAllBytes(filePath, bytes);
        }
    }
}
