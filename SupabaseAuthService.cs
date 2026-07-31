using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitToSupabasePlugin
{
    public class SupabaseAuthResult
    {
        public bool Success { get; set; }
        public string AccessToken { get; set; } = "";
        public string UserEmail { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }

    public class SupabaseTableRow
    {
        public string TableName { get; set; } = "";
        public List<Dictionary<string, string>> Rows { get; set; } = new List<Dictionary<string, string>>();
    }

    public static class SupabaseAuthService
    {
        private const string SUPABASE_URL = "https://irkrljhfbtnjspyvrapa.supabase.co";
        private const string SUPABASE_ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imlya3JsamhmYnRuanNweXZyYXBhIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzQzMTI3OTEsImV4cCI6MjA4OTg4ODc5MX0.c_Rd0hc11NRXX3aWYOODhU-e5GjGcoxobJQbBOc6LOE";

        // ─────────────────────────────────────────────────────────────────
        // AUTENTICACIÓN
        // ─────────────────────────────────────────────────────────────────

        public static SupabaseAuthResult Login(string email, string password)
        {
            using (var client = BuildClient(""))
            {
                var body = JsonConvert.SerializeObject(new { email, password });
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                try
                {
                    var response = client.PostAsync(
                        $"{SUPABASE_URL}/auth/v1/token?grant_type=password", content)
                        .GetAwaiter().GetResult();

                    var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (response.IsSuccessStatusCode)
                    {
                        var json = JObject.Parse(responseBody);
                        string token = json["access_token"]?.ToString() ?? "";
                        string userEmail = json["user"]?["email"]?.ToString() ?? email;
                        return new SupabaseAuthResult { Success = true, AccessToken = token, UserEmail = userEmail };
                    }
                    else
                    {
                        var json = JObject.Parse(responseBody);
                        string msg = json["error_description"]?.ToString()
                                     ?? json["message"]?.ToString()
                                     ?? responseBody;
                        return new SupabaseAuthResult { Success = false, ErrorMessage = msg };
                    }
                }
                catch (Exception ex)
                {
                    return new SupabaseAuthResult { Success = false, ErrorMessage = $"Error de red: {ex.Message}" };
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // LISTADO DE TABLAS
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Intenta obtener la lista de tablas vía el spec OpenAPI de Supabase REST.
        /// Si falla, devuelve la lista conocida hardcodeada.
        /// </summary>
        public static List<string> GetTableList(string accessToken)
        {
            using (var client = BuildClient(accessToken))
            {
                try
                {
                    var response = client.GetAsync($"{SUPABASE_URL}/rest/v1/")
                        .GetAwaiter().GetResult();
                    var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (response.IsSuccessStatusCode)
                    {
                        var json = JObject.Parse(body);
                        var paths = json["paths"] as JObject;
                        if (paths != null)
                        {
                            var tables = new List<string>();
                            foreach (var prop in paths.Properties())
                            {
                                string name = prop.Name.TrimStart('/');
                                if (!string.IsNullOrEmpty(name) && !name.StartsWith("rpc/"))
                                    tables.Add(name);
                            }
                            if (tables.Count > 0)
                            {
                                tables.Sort(StringComparer.OrdinalIgnoreCase);
                                return tables;
                            }
                        }
                    }
                }
                catch { /* fallback below */ }
            }

            return GetKnownTables();
        }

        public static List<string> GetKnownTables()
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

        // ─────────────────────────────────────────────────────────────────
        // FETCH DE DATOS DE TABLA
        // ─────────────────────────────────────────────────────────────────

        public static SupabaseTableRow FetchTableData(string accessToken, string tableName, int limit = 100)
        {
            var result = new SupabaseTableRow { TableName = tableName };
            using (var client = BuildClient(accessToken))
            {
                try
                {
                    var response = client.GetAsync(
                        $"{SUPABASE_URL}/rest/v1/{tableName}?limit={limit}&select=*")
                        .GetAwaiter().GetResult();

                    var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (!response.IsSuccessStatusCode) return result;

                    var rows = JArray.Parse(body);
                    foreach (var row in rows)
                    {
                        if (row is JObject obj)
                        {
                            var dict = new Dictionary<string, string>();
                            foreach (var prop in obj.Properties())
                                dict[prop.Name] = prop.Value.ToString();
                            result.Rows.Add(dict);
                        }
                    }
                }
                catch { /* return empty result */ }
            }
            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────

        private static HttpClient BuildClient(string accessToken)
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Add("apikey", SUPABASE_ANON_KEY);

            string bearer = string.IsNullOrEmpty(accessToken) ? SUPABASE_ANON_KEY : accessToken;
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearer}");
            return client;
        }
    }
}
