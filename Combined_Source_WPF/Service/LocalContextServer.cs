using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Linq;

using Combined_Source_WPF.Views;

namespace Combined_Source_WPF.Service
{
    public class LocalContextServer
    {
        private TcpListener? _listener;
        private bool _isRunning;
        private readonly Func<string> _getTaskIntent;
        private readonly Func<List<ReferenceDocument>> _getReferenceDocuments;
        private readonly Action<string> _logAction;

        // SSE 클라이언트 연결을 추적하기 위한 컬렉션
        private readonly Dictionary<string, NetworkStream> _sseClients = new Dictionary<string, NetworkStream>();
        private readonly object _lock = new object();

        public int Port { get; private set; }
        public bool IsRunning => _isRunning;

        public LocalContextServer(Func<string> getTaskIntent, Func<List<ReferenceDocument>> getReferenceDocuments, Action<string> logAction)
        {
            _getTaskIntent = getTaskIntent ?? throw new ArgumentNullException(nameof(getTaskIntent));
            _getReferenceDocuments = getReferenceDocuments ?? throw new ArgumentNullException(nameof(getReferenceDocuments));
            _logAction = logAction ?? throw new ArgumentNullException(nameof(logAction));
        }

        public void Start(int preferredPort = 15050)
        {
            if (_isRunning) return;

            int port = preferredPort;
            const int maxRetry = 100;
            bool success = false;

            for (int i = 0; i < maxRetry; i++)
            {
                try
                {
                    _listener = new TcpListener(IPAddress.Loopback, port);
                    _listener.Start();
                    
                    Port = port;
                    success = true;
                    break;
                }
                catch (SocketException)
                {
                    port++;
                }
            }

            if (!success)
            {
                throw new Exception("사용 가능한 로컬 TCP 포트를 찾을 수 없습니다.");
            }

            _isRunning = true;
            _logAction($"[Server] 표준 MCP SSE 서버 기동 완료: http://127.0.0.1:{Port}/sse");

            Task.Run(AcceptRequestsAsync);
        }

        public void Stop()
        {
            if (!_isRunning) return;

            try
            {
                _isRunning = false;
                
                lock (_lock)
                {
                    foreach (var clientStream in _sseClients.Values)
                    {
                        try { clientStream.Close(); } catch { }
                    }
                    _sseClients.Clear();
                }

                _listener?.Stop();
                _listener = null;
                _logAction("[Server] 표준 MCP 서버가 중지되었습니다.");
            }
            catch (Exception ex)
            {
                _logAction($"[Server] 서버 중지 중 오류 발생: {ex.Message}");
            }
        }

        private async Task AcceptRequestsAsync()
        {
            while (_isRunning && _listener != null)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => ProcessClientAsync(client));
                }
                catch (SocketException)
                {
                    if (!_isRunning) break;
                }
                catch (ObjectDisposedException)
                {
                    if (!_isRunning) break;
                }
                catch (Exception ex)
                {
                    _logAction($"[Server] 클라이언트 연결 대기 오류: {ex.Message}");
                }
            }
        }

        private async Task ProcessClientAsync(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            try
            {
                // 요청 버퍼 읽기
                byte[] requestBuffer = new byte[65536]; // 큰 JSON-RPC 전송을 위한 64KB 버퍼
                int bytesRead = await stream.ReadAsync(requestBuffer, 0, requestBuffer.Length);
                if (bytesRead == 0)
                {
                    client.Close();
                    return;
                }

                string requestString = Encoding.UTF8.GetString(requestBuffer, 0, bytesRead);
                string[] requestLines = requestString.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                if (requestLines.Length == 0)
                {
                    client.Close();
                    return;
                }

                string requestLine = requestLines[0];
                string[] requestParts = requestLine.Split(' ');
                if (requestParts.Length < 2)
                {
                    client.Close();
                    return;
                }

                string method = requestParts[0].ToUpper();
                string url = requestParts[1];

                // OPTIONS 요청 처리 (CORS)
                if (method == "OPTIONS")
                {
                    string corsResponse = "HTTP/1.1 200 OK\r\n" +
                                          "Access-Control-Allow-Origin: *\r\n" +
                                          "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                                          "Access-Control-Allow-Headers: Content-Type\r\n" +
                                          "Connection: close\r\n\r\n";
                    byte[] corsBytes = Encoding.UTF8.GetBytes(corsResponse);
                    await stream.WriteAsync(corsBytes, 0, corsBytes.Length);
                    client.Close();
                    return;
                }

                // URL 경로 및 쿼리 스트링 분리
                string path = url;
                string query = string.Empty;
                int queryIdx = url.IndexOf('?');
                if (queryIdx >= 0)
                {
                    path = url.Substring(0, queryIdx);
                    query = url.Substring(queryIdx + 1);
                }

                // 1. SSE 연결 수립 엔드포인트 (/sse)
                if (method == "GET" && path == "/sse")
                {
                    string clientId = Guid.NewGuid().ToString("N");
                    
                    // SSE 응답 헤더 전송
                    string sseHeader = "HTTP/1.1 200 OK\r\n" +
                                       "Content-Type: text/event-stream\r\n" +
                                       "Cache-Control: no-cache\r\n" +
                                       "Connection: keep-alive\r\n" +
                                       "Access-Control-Allow-Origin: *\r\n\r\n";
                    byte[] headerBytes = Encoding.UTF8.GetBytes(sseHeader);
                    await stream.WriteAsync(headerBytes, 0, headerBytes.Length);

                    // MCP SSE 필수 엔드포인트 이벤트 통지
                    string endpointEvent = $"event: endpoint\r\ndata: /api/message?clientId={clientId}\r\n\r\n";
                    byte[] eventBytes = Encoding.UTF8.GetBytes(endpointEvent);
                    await stream.WriteAsync(eventBytes, 0, eventBytes.Length);
                    await stream.FlushAsync();

                    _logAction($"[Server] AI 에이전트가 SSE로 연결되었습니다. (Session ID: {clientId})");

                    lock (_lock)
                    {
                        _sseClients[clientId] = stream;
                    }

                    // SSE 커넥션을 계속 유지하기 위해 핑/하트비트를 날림
                    try
                    {
                        while (_isRunning)
                        {
                            await Task.Delay(15000); // 15초마다 하트비트 전송
                            byte[] keepAliveBytes = Encoding.UTF8.GetBytes(":\r\n\r\n");
                            await stream.WriteAsync(keepAliveBytes, 0, keepAliveBytes.Length);
                            await stream.FlushAsync();
                        }
                    }
                    catch
                    {
                        // 연결이 끊어졌을 때의 처리
                    }
                    finally
                    {
                        lock (_lock)
                        {
                            _sseClients.Remove(clientId);
                        }
                        _logAction($"[Server] 에이전트 연결이 종료되었습니다. (Session ID: {clientId})");
                        client.Close();
                    }
                }
                // 2. JSON-RPC 메시지 수신 엔드포인트 (/api/message)
                else if (method == "POST" && path == "/api/message")
                {
                    // HTTP Body 추출
                    int bodyStartIndex = requestString.IndexOf("\r\n\r\n");
                    string bodyJson = string.Empty;
                    if (bodyStartIndex >= 0)
                    {
                        bodyJson = requestString.Substring(bodyStartIndex + 4);
                    }

                    // 버퍼 크기 한계로 바디가 덜 읽혔을 경우를 위한 보정
                    if (requestString.Contains("Content-Length:"))
                    {
                        // Content-Length 확인하여 스트림에서 더 읽기
                        int contentLength = ParseContentLength(requestLines);
                        int currentBodyLength = Encoding.UTF8.GetByteCount(bodyJson);
                        while (currentBodyLength < contentLength)
                        {
                            byte[] chunk = new byte[8192];
                            int read = await stream.ReadAsync(chunk, 0, chunk.Length);
                            if (read == 0) break;
                            bodyJson += Encoding.UTF8.GetString(chunk, 0, read);
                            currentBodyLength += read;
                        }
                    }

                    string jsonResponse = await HandleJsonRpcRequestAsync(bodyJson);
                    byte[] responseBytes = Encoding.UTF8.GetBytes(jsonResponse);

                    string httpResponse = "HTTP/1.1 200 OK\r\n" +
                                          "Content-Type: application/json; charset=utf-8\r\n" +
                                          "Access-Control-Allow-Origin: *\r\n" +
                                          "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                                          "Access-Control-Allow-Headers: Content-Type\r\n" +
                                          $"Content-Length: {responseBytes.Length}\r\n" +
                                          "Connection: close\r\n\r\n";

                    byte[] headerBytes = Encoding.UTF8.GetBytes(httpResponse);
                    await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
                    await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                    await stream.FlushAsync();
                    
                    client.Close();
                }
                else
                {
                    // 일반 REST API 하위 호환 및 404 폴백
                    if (method == "GET" && path == "/api/context")
                    {
                        string taskIntent = _getTaskIntent();
                        List<ReferenceDocument> refs = _getReferenceDocuments();
                        var responseData = new { task = taskIntent, approvedReferences = refs };
                        string jsonResponse = JsonSerializer.Serialize(responseData, new JsonSerializerOptions { WriteIndented = true });
                        byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonResponse);
                        string httpResponse = "HTTP/1.1 200 OK\r\n" +
                                              "Content-Type: application/json; charset=utf-8\r\n" +
                                              "Access-Control-Allow-Origin: *\r\n" +
                                              $"Content-Length: {jsonBytes.Length}\r\n" +
                                              "Connection: close\r\n\r\n";
                        await stream.WriteAsync(Encoding.UTF8.GetBytes(httpResponse));
                        await stream.WriteAsync(jsonBytes);
                    }
                    else
                    {
                        string notFoundResponse = "HTTP/1.1 404 Not Found\r\nConnection: close\r\n\r\n";
                        await stream.WriteAsync(Encoding.UTF8.GetBytes(notFoundResponse));
                    }
                    client.Close();
                }
            }
            catch (Exception ex)
            {
                _logAction($"[Server] 요청 처리 중 치명적 오류: {ex.Message}");
                try { client.Close(); } catch { }
            }
        }

        private int ParseContentLength(string[] headerLines)
        {
            foreach (var line in headerLines)
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(line.Substring(15).Trim(), out int val)) return val;
                }
            }
            return 0;
        }

        // ==========================================
        // 3. MCP JSON-RPC 2.0 프로토콜 해석 및 라우팅
        // ==========================================
        private async Task<string> HandleJsonRpcRequestAsync(string jsonRpcRequest)
        {
            object? id = null;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(jsonRpcRequest))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("id", out JsonElement idProp))
                    {
                        id = idProp.ValueKind == JsonValueKind.Number ? (object)idProp.GetInt64() : idProp.GetString();
                    }

                    string method = root.GetProperty("method").GetString() ?? string.Empty;
                    JsonElement paramsEl = root.TryGetProperty("params", out JsonElement p) ? p : default;

                    object? result = null;

                    switch (method)
                    {
                        case "initialize":
                            result = new
                            {
                                protocolVersion = "2024-11-05",
                                capabilities = new
                                {
                                    tools = new { }
                                },
                                serverInfo = new
                                {
                                    name = "WPF-Context-Feeder",
                                    version = "1.0.0"
                                }
                            };
                            break;

                        case "tools/list":
                            result = new
                            {
                                tools = new object[]
                                {
                                    new
                                    {
                                        name = "get_reference_context",
                                        description = "WPF GUI에 지정되어 있는 오늘의 에이전트 작업 목표(Task)와 허가된 소스 파일들의 본문 텍스트 내용을 한 번에 가져옵니다.",
                                        inputSchema = new
                                        {
                                            type = "object",
                                            properties = new { }
                                        }
                                    }
                                }
                            };
                            break;

                        case "tools/call":
                            string toolName = paramsEl.GetProperty("name").GetString() ?? string.Empty;
                            JsonElement argsEl = paramsEl.GetProperty("arguments");
                            result = await ExecuteToolCallAsync(toolName, argsEl);
                            break;

                        default:
                            return BuildErrorResponse(id, -32601, $"Method not found: {method}");
                    }

                    return JsonSerializer.Serialize(new
                    {
                        jsonrpc = "2.0",
                        id = id,
                        result = result
                    }, new JsonSerializerOptions { WriteIndented = true });
                }
            }
            catch (Exception ex)
            {
                return BuildErrorResponse(id, -32603, $"Internal RPC Error: {ex.Message}");
            }
        }

        private async Task<object> ExecuteToolCallAsync(string toolName, JsonElement arguments)
        {
            if (toolName == "get_reference_context")
            {
                string taskIntent = _getTaskIntent();
                List<ReferenceDocument> refs = _getReferenceDocuments();

                _logAction("[Server] 에이전트가 get_reference_context를 호출하여 문서 탐색을 시작합니다...");

                // 참조 파일들의 텍스트 내용들을 취합
                List<FileContentInfo> fileContents = await Task.Run(() => ResolveAndReadReferences(refs));

                var contentData = new
                {
                    task = taskIntent,
                    files = fileContents
                };

                string jsonContent = JsonSerializer.Serialize(contentData, new JsonSerializerOptions { WriteIndented = true });
                _logAction($"[Server] 에이전트에게 참조 문서 {fileContents.Count}개의 컨텍스트(내용)를 전달했습니다.");

                return new
                {
                    content = new object[]
                    {
                        new { type = "text", text = jsonContent }
                    }
                };
            }

            return new
            {
                isError = true,
                content = new object[]
                {
                    new { type = "text", text = $"Unknown tool: {toolName}" }
                }
            };
        }

        private List<FileContentInfo> ResolveAndReadReferences(List<ReferenceDocument> references)
        {
            var result = new List<FileContentInfo>();
            var visitedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var reference in references)
            {
                if (reference.Type == "File")
                {
                    if (File.Exists(reference.Path))
                    {
                        AddFileContent(reference.Path, result, visitedFiles);
                    }
                    else
                    {
                        _logAction($"[Server] ⚠️ 경고: 지정된 파일이 존재하지 않습니다: {reference.Path}");
                    }
                }
                else if (reference.Type == "Directory")
                {
                    if (Directory.Exists(reference.Path))
                    {
                        try
                        {
                            var files = Directory.EnumerateFiles(reference.Path, "*.*", SearchOption.AllDirectories);
                            foreach (var file in files)
                            {
                                if (IsExcludedPath(file)) continue;

                                if (IsTextFile(file))
                                {
                                    AddFileContent(file, result, visitedFiles);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logAction($"[Server] ❌ 디렉토리 탐색 오류 ({reference.Path}): {ex.Message}");
                        }
                    }
                    else
                    {
                        _logAction($"[Server] ⚠️ 경고: 지정된 디렉토리가 존재하지 않습니다: {reference.Path}");
                    }
                }
            }
            return result;
        }

        private bool IsExcludedPath(string filePath)
        {
            var parts = filePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var p = part.ToLower();
                if (p == "bin" || p == "obj" || p == ".git" || p == ".vs" || p == "node_modules" || p == "dist" || p == "out")
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsTextFile(string filePath)
        {
            try
            {
                var info = new FileInfo(filePath);
                if (info.Length > 1024 * 1024) return false; // 1MB 초과 시 제외

                string ext = Path.GetExtension(filePath).ToLower();
                string[] textExtensions = new[]
                {
                    ".cs", ".xaml", ".xml", ".json", ".txt", ".md", ".js", ".ts", ".html",
                    ".css", ".py", ".java", ".cpp", ".h", ".c", ".go", ".rs", ".yaml", ".yml",
                    ".ini", ".conf", ".sh", ".bat", ".ps1", ".sql", ".config", ".csproj", ".sln"
                };
                if (textExtensions.Contains(ext)) return true;

                // 텍스트 파일인지 확인하기 위해 처음 1024바이트를 읽어 널 바이트 유무 검사
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] buffer = new byte[1024];
                    int read = stream.Read(buffer, 0, buffer.Length);
                    for (int i = 0; i < read; i++)
                    {
                        if (buffer[i] == 0) return false; // 바이너리로 판단
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void AddFileContent(string filePath, List<FileContentInfo> result, HashSet<string> visitedFiles)
        {
            string fullPath = Path.GetFullPath(filePath);
            if (visitedFiles.Contains(fullPath)) return;
            visitedFiles.Add(fullPath);

            try
            {
                string content = File.ReadAllText(fullPath, Encoding.UTF8);
                result.Add(new FileContentInfo
                {
                    FilePath = fullPath,
                    Content = content
                });
            }
            catch (Exception ex)
            {
                _logAction($"[Server] ❌ 파일 읽기 실패 ({fullPath}): {ex.Message}");
            }
        }

        private string BuildErrorResponse(object? id, int errorCode, string message)
        {
            return JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = id,
                error = new
                {
                    code = errorCode,
                    message = message
                }
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    public class FileContentInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}
