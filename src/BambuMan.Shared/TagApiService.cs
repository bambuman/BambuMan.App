using BambuMan.Shared.Enums;
using BambuMan.Shared.Models;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;

namespace BambuMan.Shared
{
    public class TagApiService
    {
#if DEBUG
        private const string ApiUrl = "https://test.bambuman.ee/api/";
        private const string Stamp = "/xxMTXigeJVKuhfYeWFlwF1tjnFlcDFGLmAWuzIZMOs=";
#else
        private const string ApiUrl = "https://bambuman.ee/api/";
        private const string Stamp = "___HMAC_SECRET_PLACEHOLDER___";
#endif

        private readonly HttpClient httpClient;

        public Action<LogLevel, string>? LogAction { get; set; }

        public TagApiService(HttpClient httpClient)
        {
            this.httpClient = httpClient;
            this.httpClient.BaseAddress = new Uri(ApiUrl);
        }

        public async Task<(bool Success, bool RateLimited)> UploadNfcTagAsync(BambuFilamentInfo bambuFilamentInfo)
        {
            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var signature = ComputeSignature(timestamp, bambuFilamentInfo);

                var upload = new NfcTagUpload(timestamp, signature, bambuFilamentInfo.SerialNumber, bambuFilamentInfo.Identifier, bambuFilamentInfo.BlockData ?? []);

                var response = await httpClient.PutAsJsonAsync("nfc", upload);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    LogAction?.Invoke(LogLevel.Warning, "Daily tag upload limit reached (1000/day)");
                    return (false, true);
                }

                return (response.IsSuccessStatusCode, false);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                LogAction?.Invoke(LogLevel.Error, "Error on nfc upload");
            }

            return (false, false);
        }

        /// <summary>
        /// Ask the api for a newer filament match override set. Sends nothing but <paramref name="currentVersion"/> —
        /// no tag, device or user data. Returns null both when the api has nothing newer (204) and on any failure;
        /// callers treat those the same and keep the set they already have.
        /// </summary>
        public async Task<FilamentOverrideSet?> GetFilamentOverridesAsync(int currentVersion)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                var response = await httpClient.GetAsync($"filament-overrides?version={currentVersion}", cts.Token);

                if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;

                if (!response.IsSuccessStatusCode)
                {
                    LogAction?.Invoke(LogLevel.Information, $"Filament override check failed: {(int)response.StatusCode}");
                    return null;
                }

                return await response.Content.ReadFromJsonAsync<FilamentOverrideSet>(cts.Token);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                LogAction?.Invoke(LogLevel.Information, "Error checking filament overrides");
            }

            return null;
        }

        private string ComputeSignature(long timestamp, BambuFilamentInfo bambuFilamentInfo)
        {
            var message = new List<byte>();
            message.AddRange(Encoding.ASCII.GetBytes(bambuFilamentInfo.SerialNumber));
            message.AddRange(BitConverter.GetBytes(timestamp));
            message.AddRange(bambuFilamentInfo.Identifier);
            message.AddRange(bambuFilamentInfo.BlockData ?? []);
            message.AddRange(bambuFilamentInfo.Keys ?? []);

            using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(Stamp));
            var hash = hmac.ComputeHash(message.ToArray());

            return Convert.ToBase64String(hash);
        }

        public record NfcTagUpload(long Timestamp, string Signature, string SerialNumber, byte[] Uid, byte[] BlockData);
    }
}