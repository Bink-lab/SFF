using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DepotDL.CLI
{
    public sealed class RyuuDownloadResult
    {
        public bool HasZip { get; init; }
        public string? ZipPath { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public static class RyuuApiClient
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        public static RyuuDownloadResult DownloadPackage(string appId, string apiKey)
        {
            var url = $"https://generator.ryuu.lol/secure_download?appid={Uri.EscapeDataString(appId)}&auth_code={Uri.EscapeDataString(apiKey)}";
            using var response = Http.GetAsync(url).GetAwaiter().GetResult();
            var body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

            if (IsJsonResponse(contentType, body))
            {
                var message = ReadJsonMessage(body);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(message.Length == 0 ? $"Ryuu request failed with HTTP {(int)response.StatusCode}." : $"Ryuu: {message}");
                }

                return new RyuuDownloadResult
                {
                    HasZip = false,
                    Message = message.Length == 0 ? "Ryuu returned JSON without a downloadable ZIP." : $"Ryuu: {message}"
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Ryuu request failed with HTTP {(int)response.StatusCode}.");
            }

            if (body.Length == 0)
            {
                return new RyuuDownloadResult
                {
                    HasZip = false,
                    Message = "Ryuu returned an empty response."
                };
            }

            var zipPath = Path.Combine(Path.GetTempPath(), $"ryuu_{appId}_{Guid.NewGuid():N}.zip");
            File.WriteAllBytes(zipPath, body);

            return new RyuuDownloadResult
            {
                HasZip = true,
                ZipPath = zipPath,
                Message = $"Downloaded Ryuu package to {zipPath}"
            };
        }

        private static bool IsJsonResponse(string contentType, byte[] body)
        {
            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var text = Encoding.UTF8.GetString(body).TrimStart();
            return text.StartsWith("{", StringComparison.Ordinal);
        }

        private static string ReadJsonMessage(byte[] body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? string.Empty;
                }

                if (root.TryGetProperty("error", out var error))
                {
                    return error.GetString() ?? string.Empty;
                }
            }
            catch
            {
            }

            return Encoding.UTF8.GetString(body).Trim();
        }
    }
}
