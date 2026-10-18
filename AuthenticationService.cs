using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitToSupabasePlugin
{
    /// <summary>
    /// Servicio de autenticación basado en la tabla 'user_profiles' de Supabase
    /// del proyecto Review (idéntico a la Web App React / AuthContext).
    /// </summary>
    public static class AuthenticationService
    {
        private const string SupabaseUrl = "https://irkrljhfbtnjspyvrapa.supabase.co";
        private const string AnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imlya3JsamhmYnRuanNweXZyYXBhIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzQzMTI3OTEsImV4cCI6MjA4OTg4ODc5MX0.c_Rd0hc11NRXX3aWYOODhU-e5GjGcoxobJQbBOc6LOE";

        public static string AccessToken { get; private set; } = AnonKey;
        public static string UserEmail { get; private set; } = string.Empty;
        public static string UserName { get; private set; } = string.Empty;
        public static string UserId { get; private set; } = string.Empty;
        public static bool IsAdmin { get; private set; } = false;
        public static string ActiveProjectId { get; set; } = "a1234567-89ab-cdef-0123-456789abcdef";
        public static string ActiveProjectName { get; set; } = "agora click clack";
        public static bool IsLoggedIn => !string.IsNullOrEmpty(UserEmail);

        public static event Action? SessionStateChanged;

        private static string SessionFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Autodesk", "Revit", "Addins", "2027", "review_session.json"
        );

        public static void SetActiveProject(string projectId, string projectName)
        {
            if (!string.IsNullOrWhiteSpace(projectId)) ActiveProjectId = projectId;
            if (!string.IsNullOrWhiteSpace(projectName)) ActiveProjectName = projectName;
            SaveSession();
            SessionStateChanged?.Invoke();
        }

        public static async Task<System.Collections.Generic.List<SupabaseProjectItem>> FetchProjectsListAsync()
        {
            var list = new System.Collections.Generic.List<SupabaseProjectItem>();
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(8);
                client.DefaultRequestHeaders.Add("apikey", AnonKey);
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AnonKey}");

                string url = $"{SupabaseUrl}/rest/v1/projects?select=id,name,description&order=name.asc";
                var res = await client.GetAsync(url);
                if (res.IsSuccessStatusCode)
                {
                    string json = await res.Content.ReadAsStringAsync();
                    var arr = JArray.Parse(json);
                    foreach (var item in arr)
                    {
                        list.Add(new SupabaseProjectItem
                        {
                            Id = item["id"]?.ToString() ?? "",
                            Name = item["name"]?.ToString() ?? "Sin nombre",
                            Description = item["description"]?.ToString() ?? ""
                        });
                    }
                }
            }
            catch { }

            if (list.Count == 0)
            {
                list.Add(new SupabaseProjectItem { Id = "a1234567-89ab-cdef-0123-456789abcdef", Name = "agora click clack", Description = "Ágora Hotel Click Clack Medellín" });
                list.Add(new SupabaseProjectItem { Id = "1b177a64-e67d-44a8-b7d4-0a352824832b", Name = "proyecto café", Description = "Proyecto Café Especial" });
            }
            return list;
        }

        static AuthenticationService()
        {
            TryLoadSavedSession();
        }

        /// <summary>
        /// Valida las credenciales directamente contra la tabla 'user_profiles' en Supabase
        /// exactamente como lo hace la Web App (mail y password).
        /// </summary>
        public static async Task<(bool ok, string message)> LoginAsync(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return (false, "Debes ingresar tu correo y contraseña registrados en Review.");
            }

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("apikey", AnonKey);
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AnonKey}");

                string url = $"{SupabaseUrl}/rest/v1/user_profiles?mail=eq.{Uri.EscapeDataString(email.Trim().ToLower())}&password=eq.{Uri.EscapeDataString(password)}&select=id,mail,userName,admin,status";

                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    string errBody = await response.Content.ReadAsStringAsync();
                    return (false, $"Error al conectar con Supabase: {response.StatusCode} - {errBody}");
                }

                string json = await response.Content.ReadAsStringAsync();
                JArray arr = JArray.Parse(json);

                if (arr.Count == 0)
                {
                    return (false, "Credenciales inválidas. Verifica tu correo o contraseña en Review.");
                }

                JObject userObj = (JObject)arr[0];
                UserId = userObj["id"]?.ToString() ?? "";
                UserEmail = userObj["mail"]?.ToString() ?? email;
                UserName = userObj["userName"]?.ToString() ?? email.Split('@')[0];
                
                var adminVal = userObj["admin"];
                IsAdmin = adminVal != null && (adminVal.Value<bool>() == true || adminVal.ToString().Equals("true", StringComparison.OrdinalIgnoreCase));
                AccessToken = AnonKey;

                SaveSession();
                SessionStateChanged?.Invoke();

                return (true, $"¡Bienvenido {UserName}! Sesión iniciada correctamente.");
            }
            catch (Exception ex)
            {
                return (false, $"Error de red: {ex.Message}");
            }
        }

        /// <summary>
        /// Abre una interfaz web local limpia (estilo Review) en http://localhost:54321/
        /// para que el usuario inicie sesión cómodamente desde su navegador con los datos de Supabase.
        /// </summary>
        public static async Task<bool> LoginWithBrowserAsync()
        {
            try
            {
                using var listener = new HttpListener();
                listener.Prefixes.Add("http://localhost:54321/");
                listener.Start();

                string loginPageUrl = "http://localhost:54321/";
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = loginPageUrl, UseShellExecute = true });
                }
                catch
                {
                    Process.Start("explorer.exe", $"\"{loginPageUrl}\"");
                }

                bool loginSuccess = false;

                while (listener.IsListening)
                {
                    var ctx = await listener.GetContextAsync();
                    if (ctx.Request.HttpMethod == "GET")
                    {
                        // Servir formulario de login idéntico a Review
                        string html = GetLoginFormHtml();
                        byte[] buf = Encoding.UTF8.GetBytes(html);
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        ctx.Response.ContentLength64 = buf.Length;
                        await ctx.Response.OutputStream.WriteAsync(buf, 0, buf.Length);
                        ctx.Response.Close();
                    }
                    else if (ctx.Request.HttpMethod == "POST")
                    {
                        using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                        string body = await reader.ReadToEndAsync();

                        // Parsear datos de formulario (x-www-form-urlencoded)
                        var parts = body.Split('&');
                        string mail = "";
                        string pass = "";
                        foreach (var part in parts)
                        {
                            var kv = part.Split('=');
                            if (kv.Length == 2)
                            {
                                if (kv[0] == "mail") mail = WebUtility.UrlDecode(kv[1]);
                                if (kv[0] == "password") pass = WebUtility.UrlDecode(kv[1]);
                            }
                        }

                        var result = await LoginAsync(mail, pass);

                        string responseHtml = result.ok
                            ? GetSuccessHtml(UserName, UserEmail)
                            : GetErrorHtml(result.message);

                        byte[] resBuf = Encoding.UTF8.GetBytes(responseHtml);
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        ctx.Response.ContentLength64 = resBuf.Length;
                        await ctx.Response.OutputStream.WriteAsync(resBuf, 0, resBuf.Length);
                        ctx.Response.Close();

                        if (result.ok)
                        {
                            loginSuccess = true;
                            break;
                        }
                    }
                }

                listener.Stop();
                return loginSuccess;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error en LoginWeb: {ex.Message}");
                return false;
            }
        }

        public static void Logout()
        {
            UserEmail = "";
            UserName = "";
            UserId = "";
            IsAdmin = false;
            AccessToken = AnonKey;

            try
            {
                if (File.Exists(SessionFilePath))
                {
                    File.Delete(SessionFilePath);
                }
            }
            catch { }

            SessionStateChanged?.Invoke();
        }

        private static void SaveSession()
        {
            try
            {
                string dir = Path.GetDirectoryName(SessionFilePath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var session = new
                {
                    UserId,
                    UserEmail,
                    UserName,
                    IsAdmin,
                    ActiveProjectId,
                    ActiveProjectName,
                    SavedAt = DateTime.UtcNow
                };

                File.WriteAllText(SessionFilePath, JsonConvert.SerializeObject(session, Formatting.Indented));
            }
            catch { }
        }

        private static void TryLoadSavedSession()
        {
            try
            {
                if (File.Exists(SessionFilePath))
                {
                    string json = File.ReadAllText(SessionFilePath);
                    JObject obj = JObject.Parse(json);
                    UserId = obj["UserId"]?.ToString() ?? "";
                    UserEmail = obj["UserEmail"]?.ToString() ?? "";
                    UserName = obj["UserName"]?.ToString() ?? "";
                    IsAdmin = obj["IsAdmin"]?.Value<bool>() ?? false;
                    ActiveProjectId = obj["ActiveProjectId"]?.ToString() ?? "a1234567-89ab-cdef-0123-456789abcdef";
                    ActiveProjectName = obj["ActiveProjectName"]?.ToString() ?? "agora click clack";
                }
            }
            catch { }
        }

        private static string GetLoginFormHtml()
        {
            return @"<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <title>Iniciar Sesión - Review BIM</title>
  <meta name='viewport' content='width=device-width, initial-scale=1'>
  <style>
    * { box-sizing: border-box; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; }
    body { background: #f1f5f9; display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; padding: 20px; }
    .card { background: white; border-radius: 12px; box-shadow: 0 10px 25px rgba(0,0,0,0.06); width: 100%; max-width: 400px; padding: 36px; border: 1px solid #e2e8f0; }
    .logo { text-align: center; margin-bottom: 24px; }
    .logo h1 { margin: 0; font-size: 22px; color: #0f4369; font-weight: 800; letter-spacing: -0.5px; }
    .logo p { margin: 6px 0 0; color: #64748b; font-size: 13px; }
    .form-group { margin-bottom: 18px; }
    label { display: block; font-size: 13px; font-weight: 600; color: #334155; margin-bottom: 6px; }
    input { width: 100%; padding: 10px 14px; border: 1.5px solid #cbd5e1; border-radius: 8px; font-size: 14px; outline: none; transition: all 0.2s; }
    input:focus { border-color: #0f4369; box-shadow: 0 0 0 3px rgba(15,67,105,0.15); }
    button { width: 100%; background: #0f4369; color: white; border: none; border-radius: 8px; padding: 12px; font-size: 14px; font-weight: 700; cursor: pointer; transition: background 0.2s; }
    button:hover { background: #0a2d47; }
    .footer { text-align: center; margin-top: 20px; font-size: 12px; color: #94a3b8; }
  </style>
</head>
<body>
  <div class='card'>
    <div class='logo'>
      <h1>📕 ARCA REVIEW</h1>
      <p>Iniciar sesión con tu cuenta de Supabase</p>
    </div>
    <form method='POST'>
      <div class='form-group'>
        <label for='mail'>Correo Electrónico</label>
        <input type='email' id='mail' name='mail' placeholder='usuario@empresa.com' required autofocus>
      </div>
      <div class='form-group'>
        <label for='password'>Contraseña</label>
        <input type='password' id='password' name='password' placeholder='••••••••' required>
      </div>
      <button type='submit'>Ingresar a Revit</button>
    </form>
    <div class='footer'>Autodesk Revit 2027 &bull; Review Bi-MCP</div>
  </div>
</body>
</html>";
        }

        private static string GetSuccessHtml(string name, string email)
        {
            return $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'><title>Sesión Iniciada - Review</title></head>
<body style='font-family:sans-serif;text-align:center;padding:60px;background:#f8fafc;color:#1e293b;'>
  <div style='max-width:440px;margin:auto;background:white;padding:36px;border-radius:12px;box-shadow:0 10px 25px rgba(0,0,0,0.05);border:1px solid #e2e8f0;'>
    <h2 style='color:#0f4369;margin-bottom:8px;'>✅ Sesión Iniciada</h2>
    <p style='color:#475569;font-size:15px;'>¡Hola, <strong>{name}</strong>!<br>Tu cuenta (<code>{email}</code>) está conectada con Autodesk Revit.</p>
    <p style='color:#94a3b8;font-size:13px;margin-top:20px;'>Ya puedes cerrar esta ventana y regresar a Revit.</p>
  </div>
</body>
</html>";
        }

        private static string GetErrorHtml(string error)
        {
            return $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'><title>Error - Review</title></head>
<body style='font-family:sans-serif;text-align:center;padding:60px;background:#f8fafc;color:#1e293b;'>
  <div style='max-width:440px;margin:auto;background:white;padding:36px;border-radius:12px;box-shadow:0 10px 25px rgba(0,0,0,0.05);border:1px solid #fee2e2;'>
    <h2 style='color:#dc2626;margin-bottom:8px;'>❌ Error de Autenticación</h2>
    <p style='color:#475569;font-size:14px;'>{error}</p>
    <a href='/' style='display:inline-block;margin-top:16px;background:#0f4369;color:white;text-decoration:none;padding:10px 20px;border-radius:6px;font-size:13px;font-weight:bold;'>Volver a intentar</a>
  </div>
</body>
</html>";
        }
    }

    public class SupabaseProjectItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public override string ToString() => Name;
    }
}
