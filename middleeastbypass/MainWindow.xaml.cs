using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace middleeastbypass
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        string GameChatType = "";
        string WhoCanWhisperChatWithMeInExperiences = "";
        private static X509Certificate2 serverCertificate;
        private static readonly System.Net.Http.HttpClient httpClient = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        });

        async Task HandleClient(TcpClient client)
        {
            try
            {
                using (NetworkStream networkStream = client.GetStream())
                using (SslStream sslStream = new SslStream(networkStream, false))
                {
                    await sslStream.AuthenticateAsServerAsync(serverCertificate, false,
                        System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13, false);

                    byte[] buffer = new byte[16384];
                    int bytesRead = await sslStream.ReadAsync(buffer, 0, buffer.Length);

                    string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    int headerEndIndex = request.IndexOf("\r\n\r\n");

                    if (headerEndIndex == -1)
                    {
                        Debug.WriteLine("Invalid request - no header end found");
                        return;
                    }

                    string headersSection = request.Substring(0, headerEndIndex);
                    int bodyStartIndex = headerEndIndex + 4;

                    int contentLength = 0;
                    var headerLines = headersSection.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    foreach (var line in headerLines)
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            contentLength = int.Parse(line.Substring(15).Trim());
                            break;
                        }
                    }

                    int bodyBytesRead = bytesRead - bodyStartIndex;
                    if (contentLength > 0 && bodyBytesRead < contentLength)
                    {
                        byte[] bodyBuffer = new byte[contentLength];
                        Buffer.BlockCopy(buffer, bodyStartIndex, bodyBuffer, 0, bodyBytesRead);

                        while (bodyBytesRead < contentLength)
                        {
                            int remaining = contentLength - bodyBytesRead;
                            int read = await sslStream.ReadAsync(bodyBuffer, bodyBytesRead, remaining);
                            bodyBytesRead += read;
                        }

                        buffer = new byte[bodyStartIndex + contentLength];
                        Buffer.BlockCopy(Encoding.UTF8.GetBytes(headersSection + "\r\n\r\n"), 0, buffer, 0, bodyStartIndex);
                        Buffer.BlockCopy(bodyBuffer, 0, buffer, bodyStartIndex, contentLength);
                        request = Encoding.UTF8.GetString(buffer);
                    }

                    var lines = headersSection.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    var requestLine = lines[0].Split(' ');
                    string method = requestLine[0];
                    string path = requestLine[1];

                    Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] {method} {path}");

                    string body = bodyStartIndex < request.Length ? request.Substring(bodyStartIndex) : "";

                    Debug.WriteLine($"Body length: {body.Length}, Content-Length header: {contentLength}");

                    var response = await ForwardRequest(method, path, headersSection, body);

                    await sslStream.WriteAsync(response, 0, response.Length);
                    await sslStream.FlushAsync();

                    Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] Response sent ({response.Length} bytes)\n");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error: {ex.Message}");
            }
            finally
            {
                client.Close();
            }
        }

        string ModifyJsonRequest(string jsonBody)
        {
            try
            {
                dynamic obj = JObject.Parse(jsonBody);
                if (obj.status == 2)
                {
                    obj.joinScript.WhoCanWhisperChatWithMeInExperiences = WhoCanWhisperChatWithMeInExperiences;
                    obj.joinScript.GameChatType = GameChatType;
                    obj.joinScript.DataCenterId = 506;
                    //Debug.WriteLine(GameChatType);
                    //Debug.WriteLine(WhoCanWhisperChatWithMeInExperiences);
                }
                return obj.ToString(Formatting.Indented);
            } catch (Exception e)
            {
                return jsonBody;
            }
        }

        async Task<byte[]> ForwardRequest(string method, string path, string headersSection, string body)
        {
            try
            {
                string targetUrl = $"https://128.116.44.3{path}";

                var request = new HttpRequestMessage(new HttpMethod(method), targetUrl);

                var headerLines = headersSection.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in headerLines.Skip(1))
                {
                    int colonIndex = line.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        string headerName = line.Substring(0, colonIndex).Trim();
                        string headerValue = line.Substring(colonIndex + 1).Trim();

                        if (headerName.Equals("Host", StringComparison.OrdinalIgnoreCase))
                        {
                            request.Headers.Host = "gamejoin.roblox.com";
                            continue;
                        }
                        if (headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                            headerName.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                            continue;

                        try
                        {
                            if (headerName.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                                continue;
                            request.Headers.TryAddWithoutValidation(headerName, headerValue);
                        }
                        catch { }
                    }
                }

                if (!string.IsNullOrEmpty(body))
                {
                    var content = new StringContent(body, Encoding.UTF8, "application/json");
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                    request.Content = content;
                }

                var response = await httpClient.SendAsync(request);

                byte[] responseBody = await response.Content.ReadAsByteArrayAsync();
                if (response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    byte[] decompressed = responseBody;

                    if (response.Content.Headers.ContentEncoding.Contains("gzip"))
                    {
                        using (var compressedStream = new MemoryStream(responseBody))
                        using (var gzip = new GZipStream(compressedStream, CompressionMode.Decompress))
                        using (var decompressedStream = new MemoryStream())
                        {
                            gzip.CopyTo(decompressedStream);
                            decompressed = decompressedStream.ToArray();
                        }
                    }

                    string responseText = Encoding.UTF8.GetString(decompressed);
                    responseText = ModifyJsonRequest(responseText);

                    if (response.Content.Headers.ContentEncoding.Contains("gzip"))
                    {
                        using (var ms = new MemoryStream())
                        {
                            using (var gzip = new GZipStream(ms, CompressionMode.Compress, true))
                            {
                                var bytes = Encoding.UTF8.GetBytes(responseText);
                                gzip.Write(bytes, 0, bytes.Length);
                            }
                            responseBody = ms.ToArray();
                        }
                    }
                    else
                    {
                        responseBody = Encoding.UTF8.GetBytes(responseText);
                    }
                }

                var responseBuilder = new StringBuilder();
                responseBuilder.Append($"HTTP/1.1 {(int)response.StatusCode} {response.ReasonPhrase}\r\n");

                foreach (var header in response.Headers)
                {
                    if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (var value in header.Value)
                    {
                        responseBuilder.Append($"{header.Key}: {value}\r\n");
                    }
                }

                foreach (var header in response.Content.Headers)
                {
                    if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (var value in header.Value)
                    {
                        responseBuilder.Append($"{header.Key}: {value}\r\n");
                    }
                }
                responseBuilder.Append($"Content-Length: {responseBody.Length}\r\n");
                responseBuilder.Append("Connection: close\r\n");
                responseBuilder.Append("\r\n");

                byte[] responseHeaders = Encoding.UTF8.GetBytes(responseBuilder.ToString());

                byte[] fullResponse = new byte[responseHeaders.Length + responseBody.Length];
                Buffer.BlockCopy(responseHeaders, 0, fullResponse, 0, responseHeaders.Length);
                Buffer.BlockCopy(responseBody, 0, fullResponse, responseHeaders.Length, responseBody.Length);

                return fullResponse;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error forwarding request: {ex.Message}");

                string errorResponse = "HTTP/1.1 502 Bad Gateway\r\n" +
                                      "Content-Type: text/plain\r\n" +
                                      $"Content-Length: {ex.Message.Length}\r\n" +
                                      "\r\n" +
                                      ex.Message;
                return Encoding.UTF8.GetBytes(errorResponse);
            }
        }

        public async void proxythread()
        {
            string certPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gamejoin-roblox.pfx");

            if (!File.Exists(certPath))
            {
                Debug.WriteLine($"Certificate not found at: {certPath}");
                return;
            }

            try
            {
                serverCertificate = new X509Certificate2(certPath, "xd");

                Debug.WriteLine($"Certificate loaded: {serverCertificate.Subject}");
                Debug.WriteLine($"Valid from: {serverCertificate.NotBefore} to {serverCertificate.NotAfter}");
                Debug.WriteLine($"Has private key: {serverCertificate.HasPrivateKey}");

                TcpListener listener = new TcpListener(IPAddress.Any, 443);
                listener.Start();

                Debug.WriteLine("HTTPS Server started on port 443");
                Debug.WriteLine("Waiting for connections...");

                while (true)
                {
                    Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] Waiting for next connection...");
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] Connection accepted!");
                    _ = Task.Run(() => HandleClient(client));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                Debug.WriteLine("\nNote: Running on port 443 requires administrator privileges.");
            }
        }

        public async void proxystart()
        {
            Thread t = new Thread(proxythread);
            t.IsBackground = true;
            t.Start();
        }
        string homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        bool cleanupfinished = true;
        string[] rbxstorages = { "rbx-storage.db", "rbx-storage.db-shm", "rbx-storage.db-wal", "rbx-storage.id" };

        public MainWindow()
        {
            var hostsdata = File.ReadAllText("C:\\Windows\\system32\\drivers\\etc\\hosts");
            try
            {
                hostsdata = hostsdata.Replace("\n127.0.0.1 gamejoin.roblox.com", "");
                hostsdata += "\n127.0.0.1 gamejoin.roblox.com";
                File.WriteAllText("C:\\windows\\system32\\drivers\\etc\\hosts", hostsdata);
            }
            catch (Exception e)
            {
                MessageBox.Show("Run the program as Administrator.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
                return;
            }
            InitializeComponent();
            proxystart();

            Dictionary<string, string> robloxpaths = new Dictionary<string, string>
{
    { "Roblox", Path.Combine(homePath, "AppData", "Local", "Roblox", "Versions") },
    { "Bloxstrap", Path.Combine(homePath, "AppData", "Local", "Bloxstrap\\Versions") },
    { "Fishstrap", Path.Combine(homePath, "AppData", "Local", "Fishstrap\\Versions") },
    { "Voidstrap", Path.Combine(homePath, "AppData", "Local", "Voidstrap\\RblxVersions") },
    { "Plexity", Path.Combine(homePath, "AppData", "Local", "Plexity") }
};

            foreach (var kvp in robloxpaths)
            {
                string appName = kvp.Key;
                string appPath = kvp.Value;

                var versionsPath = appPath;
                if (!Directory.Exists(versionsPath))
                {
                    Console.WriteLine($"{appName} folder not found: {versionsPath}");
                    continue;
                }

                var versionFolders = new DirectoryInfo(versionsPath).GetDirectories();
                foreach (var versionFolder in versionFolders)
                {
                    Debug.WriteLine(versionFolder.FullName);
                    var exeFiles = versionFolder.GetFiles("*PlayerBeta.exe", System.IO.SearchOption.TopDirectoryOnly);
                    if (exeFiles.Length > 0)
                    {
                        Debug.WriteLine("Writing files...");
                        var sslFolder = Path.Combine(versionFolder.FullName, "ssl");
                        var sslFilePath = Path.Combine(sslFolder, "cacert.pem");

                        string sslcert = File.ReadAllText(sslFilePath);
                        string ourcert = File.ReadAllText("cert.pem");
                        if (sslcert.Contains(ourcert))
                        {
                            sslcert = sslcert.Replace(ourcert, "");
                        }
                        sslcert += ourcert;
                        File.WriteAllText(sslFilePath, sslcert);
                    }
                }
            }
            this.Closed += (s, e) =>
            {
                File.WriteAllText("C:\\windows\\system32\\drivers\\etc\\hosts", hostsdata.Replace("\n127.0.0.1 gamejoin.roblox.com", ""));
                foreach (var kvp in robloxpaths)
                {
                    string appName = kvp.Key;
                    string appPath = kvp.Value;

                    var versionsPath = appPath;
                    if (!Directory.Exists(versionsPath))
                    {
                        Debug.WriteLine($"{appName} folder not found: {versionsPath}");
                        continue;
                    }

                    var versionFolders = new DirectoryInfo(versionsPath).GetDirectories();
                    foreach (var versionFolder in versionFolders)
                    {
                        Debug.WriteLine(versionFolder.FullName);
                        var exeFiles = versionFolder.GetFiles("*PlayerBeta.exe", System.IO.SearchOption.TopDirectoryOnly);
                        if (exeFiles.Length > 0)
                        {
                            Debug.WriteLine("Writing files back...");
                            var sslFolder = Path.Combine(versionFolder.FullName, "ssl");
                            var sslFilePath = Path.Combine(sslFolder, "cacert.pem");

                            string sslcert = File.ReadAllText(sslFilePath);
                            string ourcert = File.ReadAllText("cert.pem");
                            if (sslcert.Contains(ourcert))
                            {
                                sslcert = sslcert.Replace(ourcert, "");
                            }
                            File.WriteAllText(sslFilePath, sslcert);
                        }
                    }
                }
            };
        }

        private void chatTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (chatTypeBox.SelectedItem != null)
            {
                GameChatType = chatTypeBox.SelectedValue.ToString().Replace("System.Windows.Controls.ComboBoxItem: ", ""); // workaround
            }
        }

        private void whisperTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (whisperTypeBox.SelectedItem != null)
            {
                WhoCanWhisperChatWithMeInExperiences = whisperTypeBox.SelectedValue.ToString().Replace("System.Windows.Controls.ComboBoxItem: ", "");
            }
        }

        static (string exePath, string arguments) GetAppForProtocol(string protocol)
        {
            try
            {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(protocol + @"\shell\open\command"))
                {
                    if (key != null)
                    {
                        string command = key.GetValue(null) as string; // default value
                        if (!string.IsNullOrEmpty(command))
                        {
                            string exePath = null;
                            string args = null;

                            if (command.StartsWith("\""))
                            {
                                int endQuote = command.IndexOf('"', 1);
                                if (endQuote > 1)
                                {
                                    exePath = command.Substring(1, endQuote - 1);
                                    args = command.Substring(endQuote + 1).Trim();
                                }
                            }
                            else
                            {
                                int firstSpace = command.IndexOf(' ');
                                if (firstSpace > 0)
                                {
                                    exePath = command.Substring(0, firstSpace);
                                    args = command.Substring(firstSpace + 1).Trim();
                                }
                                else
                                {
                                    exePath = command;
                                }
                            }

                            return (exePath, args);
                        }
                    }
                }
            }
            catch
            {
                // ignore errors
            }

            return (null, null);
        }

        private void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            Dictionary<string, string> robloxpaths = new Dictionary<string, string>
{
    { "Roblox", Path.Combine(homePath, "AppData", "Local", "Roblox", "Versions") },
    { "Bloxstrap", Path.Combine(homePath, "AppData", "Local", "Bloxstrap\\Versions") },
    { "Fishstrap", Path.Combine(homePath, "AppData", "Local", "Fishstrap\\Versions") },
    { "Voidstrap", Path.Combine(homePath, "AppData", "Local", "Voidstrap\\RblxVersions") },
    { "Plexity", Path.Combine(homePath, "AppData", "Local", "Plexity") }
};
            Process[] pname = Process.GetProcessesByName("RobloxPlayerBeta");
            foreach (Process p in pname)
            {
                p.Kill();
            }
            cleanupfinished = false;

            var (appExe, appArgs) = GetAppForProtocol("roblox");
            if (string.IsNullOrEmpty(appExe))
                throw new Exception("Protocol handler not found");
            string filename = Path.GetFileName(appExe);
            string app = appExe.Replace(filename, "");
            string bootstrapper = new DirectoryInfo(app).Name;
            foreach (var folder in robloxpaths)
            {
                string name = folder.Key;
                string path = folder.Value;
                if (name == bootstrapper)
                {
                    var versionFolders = new DirectoryInfo(path).GetDirectories();
                    foreach (var versionFolder in versionFolders)
                    {
                        var exeFiles = versionFolder.GetFiles("*PlayerBeta.exe", System.IO.SearchOption.TopDirectoryOnly);
                        if (exeFiles.Length > 0)
                        {
                            appExe = exeFiles[exeFiles.Length - 1].FullName;
                        }
                    }
                }
            }
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = appExe,
                Arguments = appArgs,
                UseShellExecute = true
            };
            Process proc = Process.Start(psi);
            proc.EnableRaisingEvents = true;
            //proc.Exited += (s, e) =>
            //{
            //    if (cleanupfinished) return;
            //    cleanupfinished = true;
            //    var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox\\rbx-storage");
            //    if (Directory.Exists(path))
            //    {
            //        DirectoryInfo di = new DirectoryInfo(path);
            //        foreach (FileInfo file in di.GetFiles())
            //        {
            //            file.Delete();
            //        }
            //        foreach (DirectoryInfo dir in di.GetDirectories())
            //        {
            //            dir.Delete(true);
            //        }
            //        Directory.Delete(path);
            //    }
            //    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox\\rbx-storage");
            //    foreach (string file in rbxstorages)
            //    {
            //        var pt = Path.Combine(path, file);
            //        try
            //        {
            //            File.Delete(pt);
            //        }
            //        catch (Exception ex)
            //        {
            //            Debug.WriteLine($"Couldn't delete {file}: {ex}");
            //        }
            //    }
            //    foreach (var kvp in robloxpaths)
            //    {
            //        string appName = kvp.Key;
            //        string appPath = kvp.Value;

            //        var versionsPath = appPath;
            //        if (!Directory.Exists(versionsPath))
            //        {
            //            Debug.WriteLine($"{appName} folder not found: {versionsPath}");
            //            continue;
            //        }

            //        var versionFolders = new DirectoryInfo(versionsPath).GetDirectories();
            //        foreach (var versionFolder in versionFolders)
            //        {
            //            Debug.WriteLine(versionFolder.FullName);
            //            var exeFiles = versionFolder.GetFiles("*PlayerBeta.exe", System.IO.SearchOption.TopDirectoryOnly);
            //            if (exeFiles.Length > 0)
            //            {
            //                Debug.WriteLine("Writing files back...");
            //                var sslFolder = Path.Combine(versionFolder.FullName, "ssl");
            //                var sslFilePath = Path.Combine(sslFolder, "cacert.pem");

            //                string sslcert = File.ReadAllText(sslFilePath);
            //                string ourcert = File.ReadAllText("cert.pem");
            //                if (sslcert.Contains(ourcert))
            //                {
            //                    sslcert = sslcert.Replace(ourcert, "");
            //                }
            //                File.WriteAllText(sslFilePath, sslcert);
            //            }
            //        }
            //    }
            //};
        }
    }
}