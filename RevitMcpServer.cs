using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitToSupabasePlugin
{
    /// <summary>
    /// Servidor MCP Embebido en Autodesk Revit (review_plugin_mcp).
    /// Expone el protocolo Model Context Protocol (MCP) en http://localhost:8080/mcp/
    /// para permitir a los Asistentes de IA interactuar en tiempo real con la sesión de modelado.
    /// </summary>
    public class RevitMcpServer
    {
        private HttpListener? _listener;
        private readonly McpExternalEventHandler _handler;
        private readonly ExternalEvent _externalEvent;
        private bool _isRunning = false;

        public bool IsRunning => _isRunning;
        public int Port { get; private set; } = 8080;

        public RevitMcpServer()
        {
            _handler = new McpExternalEventHandler();
            _externalEvent = ExternalEvent.Create(_handler);
        }

        public void Start(int port = 8080)
        {
            if (_isRunning) return;
            Port = port;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/mcp/");
                _listener.Start();
                _isRunning = true;
                Task.Run(ListenLoop);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error iniciando RevitMcpServer en puerto {port}: {ex.Message}");
            }
        }

        public void Stop()
        {
            _isRunning = false;
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch { }
            _listener = null;
        }

        private async Task ListenLoop()
        {
            while (_isRunning && _listener != null && _listener.IsListening)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(ctx));
                }
                catch
                {
                    if (!_isRunning) break;
                }
            }
        }

        private async Task HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                // CORS Universal
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                ctx.Response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
                ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");

                if (ctx.Request.HttpMethod == "OPTIONS")
                {
                    ctx.Response.StatusCode = 204;
                    ctx.Response.Close();
                    return;
                }

                if (ctx.Request.HttpMethod == "GET")
                {
                    // Discovery de estado para navegadores o verificación de salud
                    string info = @"{
                        ""server"": ""review-plugin-mcp"",
                        ""status"": ""online"",
                        ""platform"": ""Autodesk Revit (.NET / ExternalEvent)"",
                        ""endpoint"": ""http://localhost:" + Port + @"/mcp/"",
                        ""tools"": [
                            ""revit_get_active_session"",
                            ""revit_get_selection"",
                            ""revit_convertir_pisos"",
                            ""revit_sync_to_supabase""
                        ]
                    }";
                    byte[] infoBuf = Encoding.UTF8.GetBytes(info);
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = infoBuf.Length;
                    await ctx.Response.OutputStream.WriteAsync(infoBuf, 0, infoBuf.Length);
                    ctx.Response.Close();
                    return;
                }

                if (ctx.Request.HttpMethod == "POST")
                {
                    using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                    string body = await reader.ReadToEndAsync();
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                        return;
                    }

                    JObject rpcReq = JObject.Parse(body);
                    string? method = rpcReq["method"]?.ToString();
                    JToken? rpcId = rpcReq["id"];

                    JObject rpcRes = new JObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = rpcId
                    };

                    if (method == "initialize")
                    {
                        rpcRes["result"] = new JObject
                        {
                            ["protocolVersion"] = "2024-11-05",
                            ["capabilities"] = new JObject { ["tools"] = new JObject() },
                            ["serverInfo"] = new JObject
                            {
                                ["name"] = "review-plugin-mcp",
                                ["version"] = "2.0.0"
                            }
                        };
                    }
                    else if (method == "notifications/initialized" || method == "ping")
                    {
                        rpcRes["result"] = new JObject();
                    }
                    else if (method == "tools/list")
                    {
                        rpcRes["result"] = GetToolsManifest();
                    }
                    else if (method == "tools/call")
                    {
                        string toolName = rpcReq["params"]?["name"]?.ToString() ?? "";
                        JObject arguments = rpcReq["params"]?["arguments"] as JObject ?? new JObject();

                        // Despachar la tarea al hilo principal de Revit vía ExternalEvent
                        var taskResult = await _handler.EnqueueTaskAsync(_externalEvent, toolName, arguments);

                        rpcRes["result"] = new JObject
                        {
                            ["content"] = new JArray
                            {
                                new JObject
                                {
                                    ["type"] = "text",
                                    ["text"] = taskResult.ToString(Formatting.Indented)
                                }
                            },
                            ["isError"] = taskResult["error"] != null || (taskResult["status"]?.ToString() == "error")
                        };
                    }
                    else
                    {
                        rpcRes["error"] = new JObject
                        {
                            ["code"] = -32601,
                            ["message"] = $"Método no soportado: {method}"
                        };
                    }

                    byte[] buffer = Encoding.UTF8.GetBytes(rpcRes.ToString());
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = buffer.Length;
                    await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    ctx.Response.Close();
                    return;
                }

                ctx.Response.StatusCode = 405;
                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                try
                {
                    ctx.Response.StatusCode = 500;
                    byte[] errBuf = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = ex.Message }));
                    await ctx.Response.OutputStream.WriteAsync(errBuf, 0, errBuf.Length);
                    ctx.Response.Close();
                }
                catch { }
            }
        }

        private JObject GetToolsManifest()
        {
            return JObject.Parse(@"{
                ""tools"": [
                    {
                        ""name"": ""revit_get_active_session"",
                        ""description"": ""Obtiene información del modelo abierto en Revit (nombre del proyecto, vista activa, tipo de vista, ruta de archivo, usuario)."",
                        ""inputSchema"": {
                            ""type"": ""object"",
                            ""properties"": {}
                        }
                    },
                    {
                        ""name"": ""revit_get_selection"",
                        ""description"": ""Obtiene los elementos que el usuario tiene seleccionados en la pantalla de Revit (IDs, nombres, categorías, familias, áreas m2 y volúmenes m3)."",
                        ""inputSchema"": {
                            ""type"": ""object"",
                            ""properties"": {}
                        }
                    },
                    {
                        ""name"": ""revit_convertir_pisos"",
                        ""description"": ""Aplica el algoritmo de despiece multicapa (baldosas, mortero de pega, juntas, zócalo) a un piso en Revit mediante su ElementId."",
                        ""inputSchema"": {
                            ""type"": ""object"",
                            ""properties"": {
                                ""floor_id"": { ""type"": ""integer"", ""description"": ""ElementId del piso anfitrión en Revit"" },
                                ""material_baldosa"": { ""type"": ""string"", ""description"": ""Nombre del material de acabado / baldosa"" },
                                ""longitud_cm"": { ""type"": ""number"", ""default"": 60, ""description"": ""Largo de la pieza en cm"" },
                                ""ancho_cm"": { ""type"": ""number"", ""default"": 60, ""description"": ""Ancho de la pieza en cm"" }
                            },
                            ""required"": [""floor_id"", ""material_baldosa""]
                        }
                    },
                    {
                        ""name"": ""revit_sync_to_supabase"",
                        ""description"": ""Ejecuta la extracción y envío directo de metadata y cuantificación BIM a Supabase sin generar archivos locales intermedios."",
                        ""inputSchema"": {
                            ""type"": ""object"",
                            ""properties"": {}
                        }
                    }
                ]
            }");
        }
    }
}
