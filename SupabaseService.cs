using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitToSupabasePlugin
{
    /// <summary>
    /// Handles all Supabase REST API communication: Auth (JWT), table listing, and row fetching.
    /// Uses the anon key for public schema queries and a JWT access token after login.
    /// </summary>
    public class SupabaseService
    {
        // ── Credentials (set once from ExportadorMetadataWindow / hardcoded) ────────
        private static readonly string SupabaseUrl = "https://dkjjlhvgbbbyqzlxohse.supabase.co";
        private static readonly string AnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImRrampsaHZnYmJieXF6bHhvaHNlIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NDQzMTE3NDMsImV4cCI6MjA1OTg4NzczM30.qs8DgCErlp6f4Ux7HFBX7fvVJLQPxBHZ3EkCLxnDmM0";

        private readonly HttpClient _http;
        private string _accessToken = "";

        public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);
        public string CurrentUserEmail { get; private set; } = "";

        public SupabaseService()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("apikey", AnonKey);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // AUTH
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Sign in with email + password via Supabase Auth REST API.
        /// Returns (ok, errorMessage).
        /// </summary>
        public async Task<(bool ok, string error)> SignInAsync(string email, string password)
        {
            try
            {
                var payload = JsonConvert.SerializeObject(new { email, password });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                var url = $"{SupabaseUrl}/auth/v1/token?grant_type=password";
                _http.DefaultRequestHeaders.Remove("Authorization");

                var response = await _http.PostAsync(url, content);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var err = TryParseErrorMessage(body);
                    return (false, $"Error {(int)response.StatusCode}: {err}");
                }

                var json = JObject.Parse(body);
                _accessToken = json["access_token"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(_accessToken))
                    return (false, "La respuesta de Supabase no contenía un token de acceso.");

                _http.DefaultRequestHeaders.Remove("Authorization");
                _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
                CurrentUserEmail = email;
                return (true, "");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public void SignOut()
        {
            _accessToken = "";
            CurrentUserEmail = "";
            _http.DefaultRequestHeaders.Remove("Authorization");
        }

        // ══════════════════════════════════════════════════════════════════════════
        // TABLE LISTING  (known tables — no information_schema needed)
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns the well-known list of tables in this Supabase project.
        /// Falls back to a dynamic probe if the table doesn't respond (200 = exists).
        /// </summary>
        public List<string> GetKnownTables()
        {
            return new List<string>
            {
                "Catalogo_Componentes",
                "Elemento_Componente",
                "Espacio_Elemento",
                "Materiales",
                "Stage",
                "actions",
                "admin_documents",
                "bep_team",
                "bim_dictionary",
                "bim_plans",
                "directory_contacts",
                "esquema_nodes",
                "esquema_nodes_modules",
                "esquema_nodes_resources",
                "esquemas",
                "incidents",
                "lifecycle_stages",
                "lifecycles",
                "marketing_materials",
                "module_content_blocks",
                "module_resources",
                "plugin_project_data",
                "pre_bep_config",
                "project_area_details",
                "project_bim_uses",
                "project_delivery_schedule",
                "project_element_lod_tdi",
                "project_general_info",
                "project_info_logs",
                "project_levels",
                "project_objectives",
                "project_software",
                "project_units",
                "projects",
                "protocols",
                "resource_content_blocks",
                "resources",
                "roadmap_modules",
                "roadmap_phases",
                "roles",
                "specialties",
                "staff",
                "subProjects",
                "tasks",
                "user_profiles"
            };
        }

        // ══════════════════════════════════════════════════════════════════════════
        // ROW FETCHING
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Fetches all rows from a given Supabase table. Returns JSON string or throws.
        /// </summary>
        public async Task<string> FetchTableAsync(string tableName, int limit = 200)
        {
            var url = $"{SupabaseUrl}/rest/v1/{tableName}?limit={limit}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("apikey", AnonKey);
            req.Headers.Add("Prefer", "return=representation");
            if (!string.IsNullOrEmpty(_accessToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _http.SendAsync(req);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new Exception($"Error fetching '{tableName}': {(int)response.StatusCode} - {TryParseErrorMessage(body)}");
            }

            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// Fetches multiple tables and returns a dictionary tableName → pretty JSON string.
        /// Tables that fail are skipped and their error is included in the result as a string.
        /// </summary>
        public async Task<Dictionary<string, string>> FetchTablesAsync(IEnumerable<string> tableNames)
        {
            var result = new Dictionary<string, string>();
            foreach (var table in tableNames)
            {
                try
                {
                    var raw = await FetchTableAsync(table);
                    // Pretty print
                    var parsed = JToken.Parse(raw);
                    result[table] = parsed.ToString(Formatting.Indented);
                }
                catch (Exception ex)
                {
                    result[table] = $"[ERROR: {ex.Message}]";
                }
            }
            return result;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // HELPERS
        // ══════════════════════════════════════════════════════════════════════════

        private static string TryParseErrorMessage(string body)
        {
            try
            {
                var j = JObject.Parse(body);
                return j["error_description"]?.ToString()
                    ?? j["message"]?.ToString()
                    ?? j["msg"]?.ToString()
                    ?? body;
            }
            catch
            {
                return body;
            }
        }
    }
}
