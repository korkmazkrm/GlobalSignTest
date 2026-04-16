using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace GlobalSignTest
{
    public partial class Form1 : Form
    {
        private static readonly string ApiKey = "f8e947e6c271319a";
        private static readonly string ApiSecret = "b98ee41bc667966c1e65e46e7df5f1f2a5f674c3";
        private static readonly string MtlsPfxPath = @"C:\Temp\GlobalSign\lcw-test.pfx";
        private static readonly string MtlsPfxPassword = "lcwtest123@1";
        private static string? LastLoadedClientCertInfo;

        public Form1()
        {
            InitializeComponent();
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            try
            {
                var result = await Request1Async();
                MessageBox.Show(result);
            }
            catch (TaskCanceledException)
            {
                MessageBox.Show("İstek timeout oldu (30sn).");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex + GetCertDebugInfoOrEmpty());
            }
        }

        private async Task<string> Request1Async()
        {
            using var client = CreateMtlsClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://emea.api.dss.globalsign.com/v2/login");

            request.Headers.Add("Accept", "application/json");
            request.Content = new StringContent(BuildLoginPayload(), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var body = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();

            return body;
        }

        private static HttpClient CreateMtlsClient()
        {
            string pfxPath = ReadRequiredValue(MtlsPfxPath, "mTLS PFX yolu (MtlsPfxPath)");
            string pfxPassword = ReadRequiredValue(MtlsPfxPassword, "mTLS PFX şifresi (MtlsPfxPassword)");

            if (!File.Exists(pfxPath))
                throw new FileNotFoundException($"PFX dosyası bulunamadı: {pfxPath}");

            var cert = new X509Certificate2(
                pfxPath,
                pfxPassword,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);

            if (!cert.HasPrivateKey)
                throw new InvalidOperationException("Yüklenen mTLS sertifikasında private key bulunmuyor.");

            LastLoadedClientCertInfo =
                $"Subject: {cert.Subject}\r\n" +
                $"Thumbprint: {cert.Thumbprint}\r\n" +
                $"NotBefore: {cert.NotBefore:O}\r\n" +
                $"NotAfter: {cert.NotAfter:O}\r\n" +
                $"HasPrivateKey: {cert.HasPrivateKey}";

            var handler = new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                ClientCertificateOptions = ClientCertificateOption.Manual,
                CheckCertificateRevocationList = false
            };
            handler.ClientCertificates.Add(cert);

            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        private static string BuildLoginPayload()
        {
            string apiKey = ReadRequiredValue(ApiKey, "API key (ApiKey)");
            string apiSecret = ReadRequiredValue(ApiSecret, "API secret (ApiSecret)");

            return $$"""
            {
              "api_key": "{{apiKey}}",
              "api_secret": "{{apiSecret}}"
            }
            """;
        }

        private static string ReadRequiredValue(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"{fieldName} boş bırakılamaz.");

            return value;
        }

        private static string GetCertDebugInfoOrEmpty()
        {
            return string.IsNullOrWhiteSpace(LastLoadedClientCertInfo)
                ? string.Empty
                : "\r\n\r\n--- Loaded client certificate ---\r\n" + LastLoadedClientCertInfo;
        }
    }
}