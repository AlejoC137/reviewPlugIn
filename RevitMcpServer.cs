using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitToSupabasePlugin
{
    public class RevitMcpServer
    {
        private HttpListener? _listener;
        private readonly McpExternalEventHandler _handler;
        private readonly ExternalEvent _externalEvent;
        private readonly ConcurrentDictionary<string, HttpListenerResponse> _sseSessions = new();
        private bool _isRunning = false;

        public bool IsRunning => _isRunning;
        public int Port { get; private set; } = 8080;

        public RevitMcpServer()
        {
            _handler = new McpExternalEventHandler();
            _externalEvent = ExternalEvent.Create(_handler);
        }

        public void Start(int preferredPort = 8080)
        {
            if (_isRunning) return;

            int[] candidatePorts = new[] { preferredPort, 8081, 8082, 8085 };
            foreach (int p in candidatePorts)
            {
                HttpListener listener = new HttpListener();
                try
                {
                    listener.Prefixes.Add($"http://localhost:{p}/mcp/");
                    try
                    {
                        listener.Prefixes.Add($"http://127.0.0.1:{p}/mcp/");
                    }
                    catch
                    {
                    }

                    listener.Start();
                    _listener = listener;
                    Port = p;
                    _isRunning = true;
                    Task.Run(ListenLoop);
                    return;
                }
                catch
                {
                    try
                    {
                        listener.Close();
                        listener = new HttpListener();
                        listener.Prefixes.Add($"http://localhost:{p}/mcp/");
                        listener.Start();
                        _listener = listener;
                        Port = p;
                        _isRunning = true;
                        Task.Run(ListenLoop);
                        return;
                    }
                    catch
                    {
                        try { listener.Close(); } catch { }
                    }
                }
            }
        }

        public void Stop()
        {
            _isRunning = false;

            foreach (var kv in _sseSessions)
            {
                try
                {
                    kv.Value.Close();
                }
                catch
                {
                }
            }
            _sseSessions.Clear();

            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch
            {
            }
            _listener = null;
        }

        private async Task ListenLoop()
        {
            while (_isRunning && _listener != null && _listener.IsListening)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(ctx));
                }
                catch
                {
                    if (!_isRunning) break;
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext ctx)
        {
            try
            {
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                ctx.Response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
                ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept, Mcp-Session-Id");

                if (ctx.Request.HttpMethod == "OPTIONS")
                {
                    ctx.Response.StatusCode = 204;
                    ctx.Response.Close();
                    return;
                }

                string path = ctx.Request.Url?.AbsolutePath.TrimEnd('/') ?? "/mcp";
                string accept = ctx.Request.Headers["Accept"] ?? "";

                if (ctx.Request.HttpMethod == "GET" && (path.EndsWith("/sse", StringComparison.OrdinalIgnoreCase) || accept.Contains("text/event-stream")))
                {
                    string sessionId = Guid.NewGuid().ToString("N");
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "text/event-stream";
                    ctx.Response.Headers.Add("Cache-Control", "no-cache");
                    ctx.Response.Headers.Add("Connection", "keep-alive");

                    _sseSessions[sessionId] = ctx.Response;

                    string endpointEvent = $"event: endpoint\ndata: http://localhost:{Port}/mcp/message?sessionId={sessionId}\n\n";
                    byte[] evtBytes = Encoding.UTF8.GetBytes(endpointEvent);
                    await ctx.Response.OutputStream.WriteAsync(evtBytes, 0, evtBytes.Length);
                    await ctx.Response.OutputStream.FlushAsync();

                    while (_isRunning && _sseSessions.ContainsKey(sessionId))
                    {
                        await Task.Delay(15000);
                        try
                        {
                            byte[] keepAlive = Encoding.UTF8.GetBytes(": ping\n\n");
                            await ctx.Response.OutputStream.WriteAsync(keepAlive, 0, keepAlive.Length);
                            await ctx.Response.OutputStream.FlushAsync();
                        }
                        catch
                        {
                            _sseSessions.TryRemove(sessionId, out _);
                            break;
                        }
                    }
                    return;
                }

                if (ctx.Request.HttpMethod == "GET")
                {
                    JObject health = new JObject
                    {
                        ["server"] = "review-plugin-mcp",
                        ["version"] = "2.5.0",
                        ["status"] = "online",
                        ["platform"] = "Autodesk Revit (.NET / Concurrent ExternalEvent)",
                        ["endpoints"] = new JObject
                        {
                            ["streamable_http"] = $"http://localhost:{Port}/mcp/",
                            ["sse"] = $"http://localhost:{Port}/mcp/sse",
                            ["message"] = $"http://localhost:{Port}/mcp/message"
                        },
                        ["tools_count"] = 15
                    };
                    await WriteJsonResponseAsync(ctx.Response, 200, health.ToString(Formatting.Indented));
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
                    string? sessionId = ctx.Request.QueryString["sessionId"];

                    if (rpcId == null || rpcId.Type == JTokenType.Null)
                    {
                        ctx.Response.StatusCode = 202;
                        ctx.Response.Close();
                        return;
                    }

                    JObject rpcRes = await ProcessRpcMethodAsync(method, rpcId, rpcReq);
                    string jsonOut = rpcRes.ToString(Formatting.None);

                    if (!string.IsNullOrEmpty(sessionId) && _sseSessions.TryGetValue(sessionId, out var sseRes))
                    {
                        try
                        {
                            byte[] sseMsg = Encoding.UTF8.GetBytes($"event: message\ndata: {jsonOut}\n\n");
                            await sseRes.OutputStream.WriteAsync(sseMsg, 0, sseMsg.Length);
                            await sseRes.OutputStream.FlushAsync();
                        }
                        catch
                        {
                            _sseSessions.TryRemove(sessionId, out _);
                        }
                    }

                    await WriteJsonResponseAsync(ctx.Response, 200, jsonOut);
                    return;
                }

                ctx.Response.StatusCode = 405;
                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                try
                {
                    await WriteJsonResponseAsync(ctx.Response, 500, JsonConvert.SerializeObject(new { error = ex.Message }));
                }
                catch
                {
                }
            }
        }

        private async Task<JObject> ProcessRpcMethodAsync(string? method, JToken rpcId, JObject rpcReq)
        {
            JObject rpcRes = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = rpcId
            };

            switch (method)
            {
                case "initialize":
                    rpcRes["result"] = new JObject
                    {
                        ["protocolVersion"] = "2024-11-05",
                        ["capabilities"] = new JObject
                        {
                            ["tools"] = new JObject { ["listChanged"] = false },
                            ["resources"] = new JObject { ["listChanged"] = false },
                            ["prompts"] = new JObject { ["listChanged"] = false }
                        },
                        ["serverInfo"] = new JObject
                        {
                            ["name"] = "review-plugin-mcp",
                            ["version"] = "2.5.0"
                        }
                    };
                    break;

                case "ping":
                    rpcRes["result"] = new JObject();
                    break;

                case "resources/list":
                    rpcRes["result"] = new JObject { ["resources"] = new JArray() };
                    break;

                case "prompts/list":
                    rpcRes["result"] = new JObject { ["prompts"] = new JArray() };
                    break;

                case "tools/list":
                    rpcRes["result"] = GetToolsManifest();
                    break;

                case "tools/call":
                {
                    string toolName = rpcReq["params"]?["name"]?.ToString() ?? "";
                    JObject arguments = rpcReq["params"]?["arguments"] as JObject ?? new JObject();

                    var taskResult = await _handler.EnqueueTaskAsync(_externalEvent, toolName, arguments);
                    bool isErr = taskResult["error"] != null || (taskResult["status"]?.ToString() == "error");

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
                        ["isError"] = isErr
                    };
                    break;
                }

                default:
                    rpcRes["error"] = new JObject
                    {
                        ["code"] = -32601,
                        ["message"] = $"Método MCP no soportado: {method}"
                    };
                    break;
            }

            return rpcRes;
        }

        private static async Task WriteJsonResponseAsync(HttpListenerResponse response, int statusCode, string json)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();
        }

        private JObject GetToolsManifest()
        {
            return JObject.Parse(@"{
  ""tools"": [
    {
      ""name"": ""revit_get_active_session"",
      ""description"": ""Obtiene el estado en vivo del documento abierto en Autodesk Revit."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {}
      }
    },
    {
      ""name"": ""revit_get_selection"",
      ""description"": ""Obtiene los elementos actualmente seleccionados en la pantalla de Revit."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {}
      }
    },
    {
      ""name"": ""revit_select_elements"",
      ""description"": ""Selecciona elementos en la interfaz gráfica de Revit por sus ElementIds y enfoca la vista."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""element_ids"": {
            ""type"": ""array"",
            ""items"": { ""type"": ""integer"" }
          },
          ""zoom_to_elements"": {
            ""type"": ""boolean""
          }
        },
        ""required"": [""element_ids""]
      }
    },
    {
      ""name"": ""revit_query_elements"",
      ""description"": ""Consulta y filtra elementos en vivo dentro del modelo de Revit por categoría, nivel o texto."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""category"": { ""type"": ""string"" },
          ""level_name"": { ""type"": ""string"" },
          ""search_text"": { ""type"": ""string"" },
          ""limit"": { ""type"": ""integer"" }
        },
        ""required"": [""category""]
      }
    },
    {
      ""name"": ""revit_get_levels_and_rooms"",
      ""description"": ""Lista todos los niveles (Levels) y todas las habitaciones (Rooms) del modelo abierto."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {}
      }
    },
    {
      ""name"": ""revit_get_materials"",
      ""description"": ""Lista los materiales cargados en el proyecto activo de Revit."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""search"": { ""type"": ""string"" },
          ""limit"": { ""type"": ""integer"" }
        }
      }
    },
    {
      ""name"": ""revit_modify_parameters"",
      ""description"": ""Modifica parámetros de instancia o comentarios de uno o varios elementos en Revit."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""element_ids"": {
            ""type"": ""array"",
            ""items"": { ""type"": ""integer"" }
          },
          ""parameters"": {
            ""type"": ""object""
          }
        },
        ""required"": [""element_ids"", ""parameters""]
      }
    },
    {
      ""name"": ""revit_manage_sheets"",
      ""description"": ""Gestiona planos (Sheets) en Revit: listar, crear con TitleBlock clonado, renumerar rangos en lote mediante doble pase anti-colisión, o cargar y aplicar automáticamente la cola de cambios desde Supabase (apply_supabase_queue / open_queue_dialog)."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""action"": {
            ""type"": ""string"",
            ""enum"": [""list"", ""create"", ""shift_range"", ""renumber"", ""batch_rename"", ""apply_supabase_queue"", ""open_queue_dialog""]
          },
          ""sheet_number"": { ""type"": ""string"" },
          ""sheet_name"": { ""type"": ""string"" },
          ""template_sheet_id"": { ""type"": ""integer"" },
          ""title_block_type_id"": { ""type"": ""integer"" },
          ""clone_titleblock_from_sheet_id"": { ""type"": ""integer"" },
          ""start_number"": { ""type"": ""integer"" },
          ""end_number"": { ""type"": ""integer"" },
          ""offset"": { ""type"": ""integer"" },
          ""prefix"": { ""type"": ""string"" },
          ""renames"": {
            ""type"": ""array"",
            ""items"": {
              ""type"": ""object"",
              ""properties"": {
                ""current_number"": { ""type"": ""string"" },
                ""new_number"": { ""type"": ""string"" },
                ""new_name"": { ""type"": ""string"" }
              },
              ""required"": [""current_number"", ""new_number""]
            }
          },
          ""new_sheets"": {
            ""type"": ""array"",
            ""items"": {
              ""type"": ""object"",
              ""properties"": {
                ""sheet_number"": { ""type"": ""string"" },
                ""sheet_name"": { ""type"": ""string"" }
              },
              ""required"": [""sheet_number""]
            }
          }
        }
      }
    },
    {
      ""name"": ""revit_get_element_parameters"",
      ""description"": ""Inspecciona en profundidad todos los parámetros de instancia y de tipo de un elemento en Revit."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""element_id"": { ""type"": ""integer"" }
        },
        ""required"": [""element_id""]
      }
    },
    {
      ""name"": ""revit_manage_views_and_viewports"",
      ""description"": ""Gestiona vistas y su colocación en planos (Viewports) dentro de Revit."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""action"": {
            ""type"": ""string"",
            ""enum"": [""list"", ""duplicate"", ""add_to_sheet""]
          },
          ""view_id"": { ""type"": ""integer"" },
          ""view_type"": { ""type"": ""string"" },
          ""new_name"": { ""type"": ""string"" },
          ""duplicate_option"": {
            ""type"": ""string"",
            ""enum"": [""simple"", ""with_detailing"", ""dependent""]
          },
          ""sheet_id"": { ""type"": ""integer"" },
          ""center_x_m"": { ""type"": ""number"" },
          ""center_y_m"": { ""type"": ""number"" }
        }
      }
    },
    {
      ""name"": ""revit_delete_or_change_type"",
      ""description"": ""Elimina elementos del modelo o modifica su familia/tipo de forma segura dentro de Revit."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""action"": {
            ""type"": ""string"",
            ""enum"": [""delete"", ""change_type""]
          },
          ""element_ids"": {
            ""type"": ""array"",
            ""items"": { ""type"": ""integer"" }
          },
          ""new_type_id"": { ""type"": ""integer"" },
          ""new_type_name"": { ""type"": ""string"" }
        },
        ""required"": [""element_ids""]
      }
    },
    {
      ""name"": ""revit_apply_template_config"",
      ""description"": ""Aplica configuraciones masivas al proyecto mediante PlantillaApplicatorService."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""config_json"": { ""type"": ""string"" }
        },
        ""required"": [""config_json""]
      }
    },
    {
      ""name"": ""revit_convertir_pisos_3d"",
      ""description"": ""Modela y parametriza el despiece de baldosas sobre un piso en Revit."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""floor_id"": { ""type"": ""integer"" },
          ""material_baldosa"": { ""type"": ""string"" },
          ""longitud_cm"": { ""type"": ""number"" },
          ""ancho_cm"": { ""type"": ""number"" },
          ""espesor_cm"": { ""type"": ""number"" },
          ""junta_mm"": { ""type"": ""number"" },
          ""patron"": { ""type"": ""string"" }
        },
        ""required"": [""material_baldosa""]
      }
    },
    {
      ""name"": ""revit_crear_tablas_computos"",
      ""description"": ""Crea automáticamente las tablas de planificación (Schedules) de cómputos métricos en Revit."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""especialidad"": { ""type"": ""string"" }
        }
      }
    },
    {
      ""name"": ""revit_sync_to_supabase"",
      ""description"": ""Extrae toda la metadata BIM del documento activo en Revit y la sincroniza con Supabase."",
      ""inputSchema"": {
        ""type"": ""object"",
        ""properties"": {
          ""include_3d_scene"": { ""type"": ""boolean"" }
        }
      }
    }
  ]
}");
        }
    }
}
