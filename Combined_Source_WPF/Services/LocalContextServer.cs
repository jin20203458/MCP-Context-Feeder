using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Linq;

using Combined_Source_WPF.Models;

namespace Combined_Source_WPF.Services
{
    public class LocalContextServer
    {
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly Func<string> _getTaskIntent;
        private readonly Func<List<ReferenceDocument>> _getReferenceDocuments;
        private readonly Action<string> _logAction;
        private readonly IFileInspector _fileInspector;

        private class SseClientSession : IDisposable
        {
            private readonly HttpListenerResponse _response;
            private readonly SemaphoreSlim _writeSemaphore = new SemaphoreSlim(1, 1);

            public SseClientSession(HttpListenerResponse response)
            {
                _response = response;
            }

            public async Task SendEventAsync(string eventType, string data, CancellationToken token = default)
            {
                await _writeSemaphore.WaitAsync(token);
                try
                {
                    string sseMessage = $"event: {eventType}\ndata: {data}\n\n";
                    byte[] bytes = Encoding.UTF8.GetBytes(sseMessage);
                    await _response.OutputStream.WriteAsync(bytes, 0, bytes.Length, token);
                    await _response.OutputStream.FlushAsync(token);
                }
                finally
                {
                    _writeSemaphore.Release();
                }
            }

            public async Task SendHeartbeatAsync(CancellationToken token = default)
            {
                await _writeSemaphore.WaitAsync(token);
                try
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(":\n\n");
                    await _response.OutputStream.WriteAsync(bytes, 0, bytes.Length, token);
                    await _response.OutputStream.FlushAsync(token);
                }
                finally
                {
                    _writeSemaphore.Release();
                }
            }

            public void Dispose()
            {
                _writeSemaphore.Dispose();
                try 
                { 
                    _response.Close(); 
                } 
                catch (ObjectDisposedException) 
                {
                    // 정상 종료 흐름
                }
                catch 
                {
                    // 기타 소켓 에러 무시
                }
            }
        }

        private readonly ConcurrentDictionary<string, SseClientSession> _sseClients = new ConcurrentDictionary<string, SseClientSession>();

        public int Port { get; private set; }
        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

        public LocalContextServer(Func<string> getTaskIntent, Func<List<ReferenceDocument>> getReferenceDocuments, Action<string> logAction, IFileInspector fileInspector)
        {
            _getTaskIntent = getTaskIntent ?? throw new ArgumentNullException(nameof(getTaskIntent));
            _getReferenceDocuments = getReferenceDocuments ?? throw new ArgumentNullException(nameof(getReferenceDocuments));
            _fileInspector = fileInspector ?? throw new ArgumentNullException(nameof(fileInspector));
            
            if (logAction == null) throw new ArgumentNullException(nameof(logAction));
            _logAction = (msg) =>
            {
                logAction(msg);
                try
                {
                    string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity", "scratch");
                    string logPath = Path.Combine(logDir, "server_debug.log");
                    string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}{Environment.NewLine}";
                    File.AppendAllText(logPath, logLine, Encoding.UTF8);
                }
                catch (IOException)
                {
                    // 로그 기록 실패 시 무시
                }
                catch (UnauthorizedAccessException)
                {
                    // 권한 부족 시 무시
                }
                catch
                {
                    // 기타 무시
                }
            };
        }

        public void Start(int preferredPort = 15050)
        {
            if (IsRunning) return;

            int port = preferredPort;
            const int maxRetry = 100;
            bool success = false;

            for (int i = 0; i < maxRetry; i++)
            {
                try
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                    listener.Start();
                    
                    _listener = listener;
                    Port = port;
                    success = true;
                    break;
                }
                catch (HttpListenerException)
                {
                    port++;
                }
            }

            if (!success)
            {
                throw new Exception("사용 가능한 로컬 HTTP 포트를 찾을 수 없습니다.");
            }

            _cts = new CancellationTokenSource();
            _logAction($"[Server] 표준 MCP SSE 서버 기동 완료: http://127.0.0.1:{Port}/sse");

            CancellationToken token = _cts.Token;
            Task.Run(() => AcceptRequestsAsync(token), token);
        }

        public void Stop()
        {
            if (_cts == null) return;

            try
            {
                _cts.Cancel();
                
                foreach (var session in _sseClients.Values)
                {
                    session.Dispose();
                }
                _sseClients.Clear();

                _listener?.Stop();
                _listener = null;
                _logAction("[Server] 표준 MCP 서버가 중지되었습니다.");
            }
            catch (Exception ex)
            {
                _logAction($"[Server] 서버 중지 중 오류 발생: {ex.Message}");
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
            }
        }

        private async Task AcceptRequestsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                try
                {
                    HttpListenerContext context = await _listener.GetContextAsync();
                    _ = Task.Run(() => ProcessRequestAsync(context, token), token);
                }
                catch (HttpListenerException)
                {
                    if (token.IsCancellationRequested) break;
                }
                catch (ObjectDisposedException)
                {
                    if (token.IsCancellationRequested) break;
                }
                catch (Exception ex)
                {
                    _logAction($"[Server] 요청 수락 중 오류: {ex.Message}");
                }
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context, CancellationToken token)
        {
            var request = context.Request;
            var response = context.Response;

            // CORS 설정
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Mcp-Session-Id, Session-Id, SessionId");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = (int)HttpStatusCode.OK;
                response.Close();
                return;
            }

            string path = "/" + (request.Url?.AbsolutePath ?? "").Trim('/').ToLowerInvariant();
            string method = request.HttpMethod.ToUpperInvariant();

            _logAction($"[Server] 📥 Received Request: {method} {request.RawUrl}");

            try
            {
                // 1. SSE 연결 수립 (GET /sse)
                if (method == "GET" && path == "/sse")
                {
                    string clientId = Guid.NewGuid().ToString("N");

                    response.ContentType = "text/event-stream";
                    response.Headers.Add("Cache-Control", "no-cache");
                    response.Headers.Add("Connection", "keep-alive");
                    response.KeepAlive = true;
                    response.StatusCode = (int)HttpStatusCode.OK;

                    // 헤더 전송을 밀어내고 클라이언트 준비를 대기
                    await response.OutputStream.FlushAsync(token);
                    await Task.Delay(100, token);

                    var session = new SseClientSession(response);

                    // 상대 경로 방식으로 엔드포인트 이벤트 통지
                    await session.SendEventAsync("endpoint", $"/api/message?sessionId={clientId}", token);
                    _logAction($"[Server] AI 에이전트가 SSE로 연결되었습니다. (Session ID: {clientId})");

                    _sseClients[clientId] = session;

                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            await Task.Delay(15000, token);
                            await session.SendHeartbeatAsync(token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 취소 시 정상 종료
                    }
                    catch (Exception ex)
                    {
                        _logAction($"[Server] ⚠️ SSE 세션 오류 ({clientId}): {ex.Message}");
                    }
                    finally
                    {
                        _sseClients.TryRemove(clientId, out _);
                        session.Dispose();
                        _logAction($"[Server] 에이전트 연결이 종료되었습니다. (Session ID: {clientId})");
                    }
                }
                // 2. Streamable HTTP 요청 수락 (POST /sse)
                else if (method == "POST" && path == "/sse")
                {
                    using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                    string bodyJson = await reader.ReadToEndAsync(token);

                    _logAction($"[Server] 📥 Received POST /sse JSON-RPC Request: {bodyJson}");

                    string jsonResponse = await HandleJsonRpcRequestAsync(bodyJson);

                    _logAction($"[Server] 📤 Replying POST /sse Response: {jsonResponse}");

                    byte[] responseBytes = Encoding.UTF8.GetBytes(jsonResponse);
                    response.ContentType = "application/json; charset=utf-8";
                    response.ContentLength64 = responseBytes.Length;
                    response.StatusCode = (int)HttpStatusCode.OK;

                    await response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length, token);
                    await response.OutputStream.FlushAsync(token);
                    response.Close();
                }
                // 3. JSON-RPC 메시지 수신 엔드포인트 (/api/message)
                else if (method == "POST" && path == "/api/message")
                {
                    string sessionId = request.QueryString["sessionId"] ?? string.Empty;
                    if (string.IsNullOrEmpty(sessionId))
                    {
                        sessionId = request.Headers["Mcp-Session-Id"] 
                                 ?? request.Headers["Session-Id"] 
                                 ?? request.Headers["SessionId"] 
                                 ?? string.Empty;
                    }

                    using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                    string bodyJson = await reader.ReadToEndAsync(token);

                    _logAction($"[Server] 📥 Received POST /api/message ({sessionId}): {bodyJson}");

                    string jsonResponse = await HandleJsonRpcRequestAsync(bodyJson);

                    _sseClients.TryGetValue(sessionId, out var session);

                    if (session != null)
                    {
                        try
                        {
                            string singleLineJson = jsonResponse.Replace("\r", "").Replace("\n", "");
                            await session.SendEventAsync("message", singleLineJson, token);
                            _logAction($"[Server] 세션 '{sessionId}'의 SSE 스트림으로 JSON-RPC 응답 전송 완료.");
                        }
                        catch (Exception ex)
                        {
                            _logAction($"[Server] ❌ SSE 스트림에 응답 전송 중 오류 발생 ({sessionId}): {ex.Message}");
                        }
                    }
                    else
                    {
                        _logAction($"[Server] ⚠️ 경고: 세션 ID '{sessionId}'에 해당하는 SSE 연결 스트림을 찾을 수 없습니다.");
                    }

                    response.StatusCode = (int)HttpStatusCode.OK;
                    response.ContentLength64 = 0;
                    response.Close();
                }
                // 4. 일반 REST API (GET /api/context)
                else if (method == "GET" && path == "/api/context")
                {
                    string taskIntent = _getTaskIntent();
                    List<ReferenceDocument> refs = _getReferenceDocuments();
                    var responseData = new { task = taskIntent, approvedReferences = refs };
                    string jsonResponse = JsonSerializer.Serialize(responseData, new JsonSerializerOptions { WriteIndented = true });
                    
                    byte[] responseBytes = Encoding.UTF8.GetBytes(jsonResponse);
                    response.ContentType = "application/json; charset=utf-8";
                    response.ContentLength64 = responseBytes.Length;
                    response.StatusCode = (int)HttpStatusCode.OK;

                    await response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length, token);
                    await response.OutputStream.FlushAsync(token);
                    response.Close();
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    response.Close();
                }
            }
            catch (Exception ex)
            {
                _logAction($"[Server] ❌ 요청 처리 중 오류 발생: {ex.Message}");
                try
                {
                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    response.Close();
                }
                catch (ObjectDisposedException) { }
                catch { }
            }
        }

        private async Task<string> HandleJsonRpcRequestAsync(string jsonRpcRequest)
        {
            object? id = null;
            try
            {
                using var doc = JsonDocument.Parse(jsonRpcRequest);
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
                }, new JsonSerializerOptions { WriteIndented = false });
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
                                if (_fileInspector.IsExcludedPath(file)) continue;

                                if (_fileInspector.IsTextFile(file))
                                {
                                    AddFileContent(file, result, visitedFiles);
                                }
                            }
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            _logAction($"[Server] ❌ 디렉토리 권한 오류 ({reference.Path}): {ex.Message}");
                        }
                        catch (IOException ex)
                        {
                            _logAction($"[Server] ❌ 디렉토리 IO 오류 ({reference.Path}): {ex.Message}");
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
            catch (UnauthorizedAccessException ex)
            {
                _logAction($"[Server] ❌ 파일 읽기 권한 없음 ({fullPath}): {ex.Message}");
            }
            catch (IOException ex)
            {
                _logAction($"[Server] ❌ 파일 읽기 IO 실패 ({fullPath}): {ex.Message}");
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
            }, new JsonSerializerOptions { WriteIndented = false });
        }
    }

    public class FileContentInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}
