using BambuMan.Shared.Enums;
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

        public async Task<bool> UploadNfcTagAsync(BambuFillamentInfo bambuFillamentInfo)
        {
            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var signature = ComputeSignature(timestamp, bambuFillamentInfo);

                var upload = new NfcTagUpload(timestamp, signature, bambuFillamentInfo.SerialNumber, bambuFillamentInfo.Identifier, bambuFillamentInfo.BlockData ?? []);

                var response = await httpClient.PutAsJsonAsync("nfc", upload);

                return response.IsSuccessStatusCode;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                LogAction?.Invoke(LogLevel.Error, "Error on nfc upload");
            }

            return false;
        }

        private string ComputeSignature(long timestamp, BambuFillamentInfo bambuFillamentInfo)
        {
            var message = new List<byte>();
            message.AddRange(Encoding.ASCII.GetBytes(bambuFillamentInfo.SerialNumber));
            message.AddRange(BitConverter.GetBytes(timestamp));
            message.AddRange(bambuFillamentInfo.Identifier);
            message.AddRange(bambuFillamentInfo.BlockData ?? []);
            message.AddRange(bambuFillamentInfo.Keys ?? []);

            using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(Stamp));
            var hash = hmac.ComputeHash(message.ToArray());

            return Convert.ToBase64String(hash);
        }

        public record NfcTagUpload(long Timestamp, string Signature, string SerialNumber, byte[] Uid, byte[] BlockData);
    }
}