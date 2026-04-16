using System.Collections.ObjectModel;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using iText.Bouncycastleconnector;
using iText.Commons.Bouncycastle.Cert;
using iText.Kernel.Pdf;
using iText.Kernel.Crypto;
using iText.Signatures;

namespace GlobalSignTest
{
    public partial class Form1 : Form
    {
        private static readonly string DssBaseUrl = "https://emea.api.dss.globalsign.com:8443";
        private static readonly string ApiKey = "f8e947e6c271319a";
        private static readonly string ApiSecret = "b98ee41bc667966c1e65e46e7df5f1f2a5f674c3";
        private static readonly string MtlsPfxPath = @"C:\Temp\GlobalSign\lcw-test.pfx";
        private static readonly string MtlsPfxPassword = "lcwtest123@1";
        private static readonly string SigningIdentityId = ""; // Leave empty to auto-create identity.
        private static readonly string CreateIdentityPayloadJson = "{}"; // "{}" works for org pre-populated identities.
        private static string? LastLoadedClientCertInfo;
        private string? _selectedPdfPath;

        public Form1()
        {
            InitializeComponent();
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_selectedPdfPath))
                    throw new InvalidOperationException("Önce imzalanacak PDF dosyasını seçin.");

                var result = await RunDssFlowAsync();
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

        private async Task<string> RunDssFlowAsync()
        {
            var cert = LoadClientCertificate();
            using var client = CreateMtlsClient(cert);
            var resultBuilder = new StringBuilder();

            var loginResult = await LoginAsync(client, cert.SerialNumber);
            resultBuilder.AppendLine("Login başarılı.");
            resultBuilder.AppendLine($"Token (ilk 16): {MaskToken(loginResult.AccessToken)}");
            resultBuilder.AppendLine($"Login raw response: {loginResult.RawBody}");

            var identityBody = string.IsNullOrWhiteSpace(SigningIdentityId)
                ? await CreateIdentityAsync(client, loginResult.AccessToken)
                : await GetIdentityByIdAsync(client, loginResult.AccessToken, SigningIdentityId);
            var identity = ParseIdentity(identityBody, SigningIdentityId);
            resultBuilder.AppendLine();
            resultBuilder.AppendLine($"Identity hazır: {identity.Id}");
            resultBuilder.AppendLine($"Identity raw response: {identityBody}");

            var certPathBody = await GetCertificatePathAsync(client, loginResult.AccessToken);
            resultBuilder.AppendLine();
            resultBuilder.AppendLine("Certificate path alındı.");
            resultBuilder.AppendLine(certPathBody[..Math.Min(certPathBody.Length, 300)]);

            var outputSignedPdfPath = BuildSignedOutputPath(_selectedPdfPath!);
            var signSummary = await SignPdfAsync(
                client,
                loginResult.AccessToken,
                identity,
                certPathBody,
                _selectedPdfPath!,
                outputSignedPdfPath);
            resultBuilder.AppendLine();
            resultBuilder.AppendLine("İmzalama sonucu:");
            resultBuilder.AppendLine(signSummary);

            return resultBuilder.ToString();
        }

        private static async Task<LoginResult> LoginAsync(HttpClient client, string clientSerialNumber)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{DssBaseUrl}/v2/login");
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-SSL-Client-Serial", clientSerialNumber);
            request.Content = new StringContent(BuildLoginPayload(), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var body = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();

            var accessToken = ReadRequiredJsonProperty(body, "access_token");
            return new LoginResult(accessToken, body);
        }

        private static async Task<string> CreateIdentityAsync(HttpClient client, string accessToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{DssBaseUrl}/v2/identity");
            request.Headers.Add("Accept", "application/json");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(CreateIdentityPayloadJson, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var body = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();
            return body;
        }

        private static async Task<string> GetIdentityByIdAsync(HttpClient client, string accessToken, string identityId)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{DssBaseUrl}/v2/identity/{identityId}");
            request.Headers.Add("Accept", "application/json");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var body = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();
            return body;
        }

        private static async Task<string> GetCertificatePathAsync(HttpClient client, string accessToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{DssBaseUrl}/v2/certificate_path");
            request.Headers.Add("Accept", "application/json");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var body = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();
            return body;
        }

        private static async Task<string> SignPdfAsync(
            HttpClient client,
            string accessToken,
            IdentityResult identity,
            string certificatePathBody,
            string inputPdfPath,
            string outputSignedPdfPath)
        {
            if (!File.Exists(inputPdfPath))
                throw new FileNotFoundException($"İmzalanacak PDF bulunamadı: {inputPdfPath}");

            EnsureDirectory(outputSignedPdfPath);

            var signingCertPem = ReadRequiredValue(identity.SigningCert, "Identity signing cert");
            var caPathPem = ReadRequiredJsonProperty(certificatePathBody, "path");
            var certChain = CreateChain(signingCertPem, caPathPem);

            using var reader = new PdfReader(inputPdfPath);
            await using var os = new FileStream(outputSignedPdfPath, FileMode.Create, FileAccess.Write);
            var signer = new PdfSigner(reader, os, new StampingProperties());

            var container = new GlobalSignExternalSignatureContainer(
                certChain,
                identity.OcspResponse,
                accessToken,
                identity.Id,
                client);

            signer.SignExternalContainer(container, 12000);

            return
                $"İmzalı PDF yazıldı: {outputSignedPdfPath}\r\n" +
                $"Identity: {identity.Id}";
        }

        private void buttonSelectPdf_Click(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "İmzalanacak PDF dosyasını seçin",
                Filter = "PDF files (*.pdf)|*.pdf",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _selectedPdfPath = dialog.FileName;
                labelSelectedFile.Text = _selectedPdfPath;
            }
        }

        private static async Task<string> SignDigestAsync(HttpClient client, string accessToken, string identityId, string digestHex)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{DssBaseUrl}/v2/identity/{identityId}/sign/{digestHex}");
            request.Headers.Add("Accept", "application/json");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var body = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();
            return body;
        }

        private static async Task<string> GetTimestampAsync(HttpClient client, string accessToken, string digestHex)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{DssBaseUrl}/v2/timestamp/{digestHex}");
            request.Headers.Add("Accept", "application/json");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var body = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();
            return body;
        }

        private static HttpClient CreateMtlsClient(X509Certificate2 cert)
        {
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

        private static X509Certificate2 LoadClientCertificate()
        {
            string pfxPath = ReadRequiredValue(MtlsPfxPath, "mTLS PFX yolu (MtlsPfxPath)");
            string pfxPassword = ReadRequiredValue(MtlsPfxPassword, "mTLS PFX şifresi (MtlsPfxPassword)");

            if (!File.Exists(pfxPath))
                throw new FileNotFoundException($"PFX dosyası bulunamadı: {pfxPath}");

            var cert = new X509Certificate2(
                pfxPath,
                pfxPassword,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);

            return cert;
        }

        private static string ReadRequiredJsonProperty(string json, string propertyName)
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(propertyName, out var property) ||
                property.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(property.GetString()))
            {
                throw new InvalidOperationException($"Response içinde `{propertyName}` bulunamadı.");
            }

            return property.GetString()!;
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

        private static IX509Certificate[] CreateChain(string signingCertPem, string caPathPem)
        {
            var factory = BouncyCastleFactoryCreator.GetFactory();
            var chain = new List<IX509Certificate>();

            using (var certStream = new MemoryStream(Encoding.UTF8.GetBytes(signingCertPem)))
            {
                chain.Add(factory.CreateX509Certificate(certStream));
            }

            var parser = factory.CreateX509CertificateParser();
            foreach (var cert in parser.ReadAllCerts(Encoding.UTF8.GetBytes(caPathPem)))
            {
                chain.Add(cert);
            }

            return chain.ToArray();
        }

        private static IdentityResult ParseIdentity(string identityBody, string configuredIdentityId)
        {
            using var document = JsonDocument.Parse(identityBody);
            var root = document.RootElement;
            var id = TryGetString(root, "id", out var fromBody)
                ? fromBody
                : ReadRequiredValue(configuredIdentityId, "Signing identity id (SigningIdentityId)");
            var signingCert = TryGetString(root, "signing_cert", out var cert) ? cert : string.Empty;
            var ocspResponse = TryGetString(root, "ocsp_response", out var ocsp) ? ocsp : string.Empty;
            return new IdentityResult(id, signingCert, ocspResponse);
        }

        private static string ReadRequiredValue(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"{fieldName} boş bırakılamaz.");

            return value;
        }

        private static string MaskToken(string token)
        {
            return token.Length <= 16 ? token : token[..16] + "...";
        }

        private static void EnsureDirectory(string filePath)
        {
            var outputDir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(outputDir))
                Directory.CreateDirectory(outputDir);
        }

        private static string BuildSignedOutputPath(string inputPdfPath)
        {
            var directory = Path.GetDirectoryName(inputPdfPath) ?? string.Empty;
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputPdfPath);
            return Path.Combine(directory, $"{fileNameWithoutExtension}.signed.pdf");
        }

        private static bool TryGetString(JsonElement element, string propertyName, out string value)
        {
            value = string.Empty;
            if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.String)
                return false;

            var str = prop.GetString();
            if (string.IsNullOrWhiteSpace(str))
                return false;

            value = str;
            return true;
        }

        private static string GetCertDebugInfoOrEmpty()
        {
            return string.IsNullOrWhiteSpace(LastLoadedClientCertInfo)
                ? string.Empty
                : "\r\n\r\n--- Loaded client certificate ---\r\n" + LastLoadedClientCertInfo;
        }

        private sealed record LoginResult(string AccessToken, string RawBody);
        private sealed record IdentityResult(string Id, string SigningCert, string OcspResponse);

        private sealed class GlobalSignExternalSignatureContainer : IExternalSignatureContainer
        {
            private readonly IX509Certificate[] _chain;
            private readonly string _accessToken;
            private readonly string _identityId;
            private readonly HttpClient _client;

            public GlobalSignExternalSignatureContainer(
                IX509Certificate[] chain,
                string ocspResponseBase64,
                string accessToken,
                string identityId,
                HttpClient client)
            {
                _chain = chain;
                _accessToken = accessToken;
                _identityId = identityId;
                _client = client;
            }

            public byte[] Sign(Stream data)
            {
                var hashAlgorithm = DigestAlgorithms.SHA256;
                var digest = DigestAlgorithms.GetMessageDigest(hashAlgorithm);
                var hash = DigestAlgorithms.Digest(data, digest);

                var pkcs7 = new PdfPKCS7(null, _chain, hashAlgorithm, false);
                var ocspCollection = new Collection<byte[]>();

                var authenticatedAttributes = pkcs7.GetAuthenticatedAttributeBytes(
                    hash,
                    PdfSigner.CryptoStandard.CADES,
                    ocspCollection,
                    null);

                var digestHex = Convert.ToHexString(SHA256.HashData(authenticatedAttributes)).ToLowerInvariant();
                var signatureHex = RequestSignatureHex(digestHex);
                var signedHash = Convert.FromHexString(signatureHex);

                pkcs7.SetExternalSignatureValue(signedHash, null, "RSA");
                return pkcs7.GetEncodedPKCS7(hash, PdfSigner.CryptoStandard.CADES, null, ocspCollection, null);
            }

            public void ModifySigningDictionary(iText.Kernel.Pdf.PdfDictionary signDic)
            {
                signDic.Put(iText.Kernel.Pdf.PdfName.Filter, iText.Kernel.Pdf.PdfName.Adobe_PPKLite);
                signDic.Put(iText.Kernel.Pdf.PdfName.SubFilter, iText.Kernel.Pdf.PdfName.Adbe_pkcs7_detached);
            }

            private string RequestSignatureHex(string digestHex)
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{DssBaseUrl}/v2/identity/{_identityId}/sign/{digestHex}");
                request.Headers.Add("Accept", "application/json");
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

                using var response = _client.Send(request);
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                return ReadRequiredJsonProperty(body, "signature");
            }
        }
    }
}