using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitToSupabasePlugin.Models;
using Application = Autodesk.Revit.ApplicationServices.Application;

namespace RevitToSupabasePlugin.Services
{
    public static class SharedParameterCloudService
    {
        private const string SupabaseUrl = "https://irkrljhfbtnjspyvrapa.supabase.co";
        private const string AnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imlya3JsamhmYnRuanNweXZyYXBhIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzQzMTI3OTEsImV4cCI6MjA4OTg4ODc5MX0.c_Rd0hc11NRXX3aWYOODhU-e5GjGcoxobJQbBOc6LOE";
        private const string DocTypeTag = "revit_shared_parameters_txt";

        public static string LocalLibraryFolder
        {
            get
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Autodesk", "Revit", "Addins", "ARCA_SharedParameters");
                if (!Directory.Exists(folder))
                {
                    try { Directory.CreateDirectory(folder); } catch { }
                }
                return folder;
            }
        }

        private static string LocalIndexJsonPath => Path.Combine(LocalLibraryFolder, "cloud_cache_index.json");

        /// <summary>
        /// Garantiza que exista un archivo .txt estándar ARCA local y lo devuelve.
        /// </summary>
        public static string EnsureDefaultSharedParamFile(Application app, bool activateIfEmpty = true)
        {
            string defaultPath = Path.Combine(LocalLibraryFolder, "ARCA_Estandar_BIM.txt");
            if (!File.Exists(defaultPath))
            {
                File.WriteAllText(defaultPath, BuildDefaultArcaTxtContent(), Encoding.UTF8);
            }

            if (activateIfEmpty && (string.IsNullOrWhiteSpace(app.SharedParametersFilename) || !File.Exists(app.SharedParametersFilename)))
            {
                app.SharedParametersFilename = defaultPath;
            }

            return defaultPath;
        }

        public static string BuildDefaultArcaTxtContent()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# This is a Revit shared parameter file.");
            sb.AppendLine("# Do not edit manually.");
            sb.AppendLine("*META\tVERSION\tMINVERSION");
            sb.AppendLine("META\t2\t1");
            sb.AppendLine("*GROUP\tID\tNAME");
            sb.AppendLine("GROUP\t1\t01_ARCA_Identificacion");
            sb.AppendLine("GROUP\t2\t02_ARCA_Cantidades_Costos");
            sb.AppendLine("GROUP\t3\t03_ARCA_Calidad_QAQC");
            sb.AppendLine("GROUP\t4\t04_ARCA_Dimensiones_Modulares");
            sb.AppendLine("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE");
            sb.AppendLine("PARAM\t10000001-aaaa-4bbb-8ccc-000000000001\tARCA_Codigo_BIM\tTEXT\t\t1\t1\tCódigo único clasificador ARCA BIM\t1\t0");
            sb.AppendLine("PARAM\t10000001-aaaa-4bbb-8ccc-000000000002\tARCA_Sector\tTEXT\t\t1\t1\tSector o zona constructiva del elemento\t1\t0");
            sb.AppendLine("PARAM\t10000001-aaaa-4bbb-8ccc-000000000003\tARCA_Frente_Obra\tTEXT\t\t1\t1\tFrente de ejecución o contratista\t1\t0");
            sb.AppendLine("PARAM\t10000001-aaaa-4bbb-8ccc-000000000004\tARCA_Partida_Presupuesto\tTEXT\t\t2\t1\tÍtem o APU de presupuesto vinculado\t1\t0");
            sb.AppendLine("PARAM\t10000001-aaaa-4bbb-8ccc-000000000005\tARCA_Estado_QAQC\tTEXT\t\t3\t1\tEstado de auditoría e inspección BIM\t1\t0");
            sb.AppendLine("PARAM\t10000001-aaaa-4bbb-8ccc-000000000006\tARCA_Auditado\tYESNO\t\t3\t1\tIndica si el elemento fue validado en QA/QC\t1\t0");
            sb.AppendLine("PARAM\t10000001-aaaa-4bbb-8ccc-000000000007\tLargo\tLENGTH\t\t4\t1\tDimensión modular de largo\t1\t0");
            sb.AppendLine("PARAM\t10000001-aaaa-4bbb-8ccc-000000000008\tAlto\tLENGTH\t\t4\t1\tDimensión modular de alto\t1\t0");
            sb.AppendLine("PARAM\t10000001-aaaa-4bbb-8ccc-000000000009\tProfundidad\tLENGTH\t\t4\t1\tDimensión modular de espesor o profundidad\t1\t0");
            return sb.ToString();
        }

        /// <summary>
        /// Lee todas las definiciones del archivo .txt activo o de una ruta específica, y cruza con el documento activo.
        /// </summary>
        public static List<SharedParamTxtItem> ReadSharedParameterFileItems(Application app, Document? activeDoc, string? customFilePath = null)
        {
            var result = new List<SharedParamTxtItem>();
            string targetPath = !string.IsNullOrWhiteSpace(customFilePath)
                ? customFilePath!
                : app.SharedParametersFilename;

            if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
                return result;

            // Recopilar GUIDs presentes en el documento activo
            var docGuids = new HashSet<Guid>();
            if (activeDoc != null && !activeDoc.IsFamilyDocument)
            {
                var spElems = new FilteredElementCollector(activeDoc)
                    .OfClass(typeof(SharedParameterElement))
                    .Cast<SharedParameterElement>();
                foreach (var sp in spElems)
                {
                    try { docGuids.Add(sp.GuidValue); } catch { }
                }
            }
            else if (activeDoc != null && activeDoc.IsFamilyDocument)
            {
                try
                {
                    foreach (FamilyParameter fp in activeDoc.FamilyManager.Parameters)
                    {
                        if (fp.IsShared)
                        {
                            try { docGuids.Add(fp.GUID); } catch { }
                        }
                    }
                }
                catch { }
            }

            // Parsear directamente el archivo .txt (más tolerante y rápido, además de leer metadatos completos)
            var groupsById = new Dictionary<string, string>();
            var lines = File.ReadAllLines(targetPath);

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("*"))
                    continue;

                var parts = rawLine.Split('\t');
                if (parts.Length >= 3 && parts[0].Equals("GROUP", StringComparison.OrdinalIgnoreCase))
                {
                    groupsById[parts[1].Trim()] = parts[2].Trim();
                }
                else if (parts.Length >= 6 && parts[0].Equals("PARAM", StringComparison.OrdinalIgnoreCase))
                {
                    Guid.TryParse(parts[1].Trim(), out Guid g);
                    string pName = parts[2].Trim();
                    string dataType = parts[3].Trim();
                    string groupId = parts[5].Trim();
                    bool visible = parts.Length <= 6 || parts[6].Trim() != "0";
                    string desc = parts.Length > 7 ? parts[7].Trim() : "";
                    bool userMod = parts.Length <= 8 || parts[8].Trim() != "0";
                    bool hideNoVal = parts.Length > 9 && parts[9].Trim() == "1";

                    string groupName = groupsById.TryGetValue(groupId, out string? gn) ? gn : $"Grupo_{groupId}";

                    result.Add(new SharedParamTxtItem
                    {
                        GroupName = groupName,
                        Name = pName,
                        Guid = g,
                        DataTypeLabel = dataType,
                        SpecTypeIdRaw = dataType,
                        Description = desc,
                        Visible = visible,
                        UserModifiable = userMod,
                        HideWhenNoValue = hideNoVal,
                        ExistsInActiveDoc = g != Guid.Empty && docGuids.Contains(g)
                    });
                }
            }

            return result.OrderBy(r => r.GroupName).ThenBy(r => r.Name).ToList();
        }

        /// <summary>
        /// Rescata todos los SharedParameterElement del proyecto actual que no están en el archivo .txt activo
        /// y los inyecta en el archivo .txt manteniendo su GUID original exacto.
        /// </summary>
        public static int RescueOrphanSharedParametersToTxt(Application app, Document doc, string targetGroupName = "99_ARCA_Rescatados_RVT")
        {
            if (doc == null || doc.IsFamilyDocument) return 0;

            string txtPath = EnsureDefaultSharedParamFile(app, true);
            var existingItems = ReadSharedParameterFileItems(app, doc, txtPath);
            var existingGuids = new HashSet<Guid>(existingItems.Where(x => x.Guid != Guid.Empty).Select(x => x.Guid));

            var spElements = new FilteredElementCollector(doc)
                .OfClass(typeof(SharedParameterElement))
                .Cast<SharedParameterElement>()
                .ToList();

            var orphans = new List<(Guid guid, string name, string dataTypeToken)>();
            foreach (var sp in spElements)
            {
                Guid g;
                try { g = sp.GuidValue; } catch { continue; }
                if (existingGuids.Contains(g)) continue;

                var def = sp.GetDefinition();
                if (def == null) continue;

                string dtToken = ConvertForgeDataTypeToTxtToken(def.GetDataType());
                orphans.Add((g, def.Name, dtToken));
                existingGuids.Add(g);
            }

            if (orphans.Count == 0) return 0;

            AppendParametersWithKnownGuidsToTxtFile(txtPath, targetGroupName, orphans);
            // Reasignar en Revit para refrescar caché
            app.SharedParametersFilename = txtPath;
            return orphans.Count;
        }

        /// <summary>
        /// Agrega parámetros con GUIDs específicos directamente al archivo .txt de Revit (ideal para rescate de huérfanos y fusión desde la nube).
        /// </summary>
        public static void AppendParametersWithKnownGuidsToTxtFile(
            string txtPath,
            string groupName,
            List<(Guid guid, string name, string dataTypeToken)> parametersToAdd)
        {
            if (!File.Exists(txtPath))
            {
                File.WriteAllText(txtPath, BuildDefaultArcaTxtContent(), Encoding.UTF8);
            }

            var lines = File.ReadAllLines(txtPath).ToList();
            var groupsById = new Dictionary<int, string>();
            int targetGroupId = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                var parts = lines[i].Split('\t');
                if (parts.Length >= 3 && parts[0].Equals("GROUP", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(parts[1].Trim(), out int gid))
                    {
                        string gName = parts[2].Trim();
                        groupsById[gid] = gName;
                        if (gName.Equals(groupName, StringComparison.OrdinalIgnoreCase))
                        {
                            targetGroupId = gid;
                        }
                    }
                }
            }

            if (targetGroupId == -1)
            {
                targetGroupId = groupsById.Count == 0 ? 1 : groupsById.Keys.Max() + 1;
                // Insertar antes de *PARAM
                int paramHeaderIdx = lines.FindIndex(l => l.TrimStart().StartsWith("*PARAM", StringComparison.OrdinalIgnoreCase));
                string newGroupLine = $"GROUP\t{targetGroupId}\t{groupName}";
                if (paramHeaderIdx >= 0)
                    lines.Insert(paramHeaderIdx, newGroupLine);
                else
                    lines.Add(newGroupLine);
            }

            foreach (var p in parametersToAdd)
            {
                string cleanName = p.name.Replace("\t", " ").Trim();
                string dt = string.IsNullOrWhiteSpace(p.dataTypeToken) ? "TEXT" : p.dataTypeToken.Trim().ToUpperInvariant();
                string paramLine = $"PARAM\t{p.guid}\t{cleanName}\t{dt}\t\t{targetGroupId}\t1\tRescatado/Sincronizado por ARCA Review\t1\t0";
                lines.Add(paramLine);
            }

            File.WriteAllLines(txtPath, lines, Encoding.UTF8);
        }

        public static string ConvertForgeDataTypeToTxtToken(ForgeTypeId? dataType)
        {
            if (dataType == null || dataType.Empty()) return "TEXT";
            if (dataType == SpecTypeId.String.Text) return "TEXT";
            if (dataType == SpecTypeId.String.MultilineText) return "MULTILINETEXT";
            if (dataType == SpecTypeId.String.Url) return "URL";
            if (dataType == SpecTypeId.Boolean.YesNo) return "YESNO";
            if (dataType == SpecTypeId.Int.Integer) return "INTEGER";
            if (dataType == SpecTypeId.Number) return "NUMBER";
            if (dataType == SpecTypeId.Length) return "LENGTH";
            if (dataType == SpecTypeId.Area) return "AREA";
            if (dataType == SpecTypeId.Volume) return "VOLUME";
            if (dataType == SpecTypeId.Angle) return "ANGLE";
            if (dataType == SpecTypeId.Slope) return "SLOPE";
            if (dataType == SpecTypeId.Currency) return "CURRENCY";
            if (dataType == SpecTypeId.MassDensity) return "MASS_DENSITY";
            if (dataType == SpecTypeId.Reference.Material) return "MATERIAL";
            if (dataType == SpecTypeId.Reference.Image) return "IMAGE";
            return "TEXT";
        }

        public static ForgeTypeId ConvertTxtTokenToForgeDataType(string? token)
        {
            string t = (token ?? "TEXT").Trim().ToUpperInvariant();
            return t switch
            {
                "TEXT" => SpecTypeId.String.Text,
                "MULTILINETEXT" => SpecTypeId.String.MultilineText,
                "URL" => SpecTypeId.String.Url,
                "YESNO" or "BOOLEAN" or "SI/NO" => SpecTypeId.Boolean.YesNo,
                "INTEGER" or "ENTERO" => SpecTypeId.Int.Integer,
                "NUMBER" or "NUMERO" => SpecTypeId.Number,
                "LENGTH" or "LONGITUD" => SpecTypeId.Length,
                "AREA" or "ÁREA" => SpecTypeId.Area,
                "VOLUME" or "VOLUMEN" => SpecTypeId.Volume,
                "ANGLE" or "ÁNGULO" => SpecTypeId.Angle,
                "SLOPE" or "PENDIENTE" => SpecTypeId.Slope,
                "CURRENCY" or "MONEDA" => SpecTypeId.Currency,
                "MATERIAL" => SpecTypeId.Reference.Material,
                _ => SpecTypeId.String.Text
            };
        }

        // ══════════════════════════════════════════════════════════════════════════
        // SUPABASE CLOUD SYNC & BACKUP DE ARCHIVOS .TXT
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Sube o respalda un archivo .txt de Parámetros Compartidos a Supabase (tabla admin_documents) y actualiza el caché local.
        /// </summary>
        public static async Task<(bool ok, string message, CloudSharedParamFileRecord? record)> UploadSharedParamFileToCloudAsync(
            string localTxtFilePath,
            string profileTitle,
            string projectName)
        {
            if (string.IsNullOrWhiteSpace(localTxtFilePath) || !File.Exists(localTxtFilePath))
                return (false, "El archivo .txt seleccionado no existe en disco.", null);

            string contentTxt = File.ReadAllText(localTxtFilePath, Encoding.UTF8);
            var lines = contentTxt.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int groupCount = lines.Count(l => l.StartsWith("GROUP\t", StringComparison.OrdinalIgnoreCase));
            int paramCount = lines.Count(l => l.StartsWith("PARAM\t", StringComparison.OrdinalIgnoreCase));

            var rec = new CloudSharedParamFileRecord
            {
                Id = Guid.NewGuid().ToString(),
                FileName = Path.GetFileName(localTxtFilePath),
                ProfileTitle = string.IsNullOrWhiteSpace(profileTitle) ? Path.GetFileNameWithoutExtension(localTxtFilePath) : profileTitle.Trim(),
                ProjectName = string.IsNullOrWhiteSpace(projectName) ? AuthenticationService.ActiveProjectName : projectName.Trim(),
                AuthorEmail = AuthenticationService.IsLoggedIn ? AuthenticationService.UserEmail : "local@arcareview.bim",
                UpdatedAt = DateTime.Now,
                ParamCount = paramCount,
                GroupCount = groupCount,
                ContentTxt = contentTxt,
                SourceKind = "Supabase Cloud"
            };

            // Guardar primero en caché local de perfiles
            SaveToLocalCache(rec);

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                client.DefaultRequestHeaders.Add("apikey", AnonKey);
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AnonKey}");
                client.DefaultRequestHeaders.Add("Prefer", "return=representation");

                var payloadObj = new
                {
                    profile_title = rec.ProfileTitle,
                    file_name = rec.FileName,
                    project_name = rec.ProjectName,
                    author_email = rec.AuthorEmail,
                    updated_at = DateTime.UtcNow.ToString("o"),
                    param_count = rec.ParamCount,
                    group_count = rec.GroupCount,
                    content_txt = rec.ContentTxt
                };

                var rowBody = new Dictionary<string, object?>
                {
                    ["title"] = $"SHARED_PARAMS :: {rec.ProfileTitle} ({rec.FileName})",
                    ["type"] = DocTypeTag,
                    ["content"] = JsonConvert.SerializeObject(payloadObj)
                };

                if (Guid.TryParse(AuthenticationService.ActiveProjectId, out Guid projGuid))
                {
                    rowBody["project_id"] = projGuid.ToString();
                }

                string url = $"{SupabaseUrl}/rest/v1/admin_documents";
                var resp = await client.PostAsync(
                    url,
                    new StringContent(JsonConvert.SerializeObject(rowBody), Encoding.UTF8, "application/json"));

                if (resp.IsSuccessStatusCode)
                {
                    return (true, $"Respaldo exitoso en Supabase Cloud: '{rec.ProfileTitle}' ({rec.ParamCount} parámetros).", rec);
                }
                else
                {
                    string err = await resp.Content.ReadAsStringAsync();
                    rec.SourceKind = "Biblioteca Local (Offline Backup)";
                    return (true, $"Guardado en Biblioteca Local de ARCA (Aviso Supabase {resp.StatusCode}: {err}).", rec);
                }
            }
            catch (Exception ex)
            {
                rec.SourceKind = "Biblioteca Local (Offline Backup)";
                return (true, $"Guardado en Biblioteca Local de ARCA (Modo Offline: {ex.Message}).", rec);
            }
        }

        /// <summary>
        /// Obtiene todos los perfiles de archivos .txt de Parámetros Compartidos desde Supabase + Biblioteca Local.
        /// </summary>
        public static async Task<List<CloudSharedParamFileRecord>> FetchCloudProfilesAsync()
        {
            var results = new List<CloudSharedParamFileRecord>();

            // 1. Intentar descargar de Supabase
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
                client.DefaultRequestHeaders.Add("apikey", AnonKey);
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AnonKey}");

                string url = $"{SupabaseUrl}/rest/v1/admin_documents?type=eq.{DocTypeTag}&order=created_at.desc&limit=30";
                var resp = await client.GetAsync(url);
                if (resp.IsSuccessStatusCode)
                {
                    string json = await resp.Content.ReadAsStringAsync();
                    var arr = JArray.Parse(json);
                    foreach (var item in arr)
                    {
                        try
                        {
                            string docId = item["id"]?.ToString() ?? Guid.NewGuid().ToString();
                            string contentRaw = item["content"]?.ToString() ?? "{}";
                            var jObj = JObject.Parse(contentRaw);

                            string contentTxt = jObj["content_txt"]?.ToString() ?? "";
                            if (string.IsNullOrWhiteSpace(contentTxt)) continue;

                            DateTime.TryParse(jObj["updated_at"]?.ToString() ?? item["created_at"]?.ToString(), out DateTime dt);
                            if (dt == default) dt = DateTime.Now;

                            results.Add(new CloudSharedParamFileRecord
                            {
                                Id = docId,
                                ProfileTitle = jObj["profile_title"]?.ToString() ?? item["title"]?.ToString() ?? "Perfil Cloud",
                                FileName = jObj["file_name"]?.ToString() ?? "ARCA_Cloud_SharedParams.txt",
                                ProjectName = jObj["project_name"]?.ToString() ?? "Global",
                                AuthorEmail = jObj["author_email"]?.ToString() ?? "equipo@arcareview.bim",
                                UpdatedAt = dt.ToLocalTime(),
                                ParamCount = jObj["param_count"]?.Value<int>() ?? 0,
                                GroupCount = jObj["group_count"]?.Value<int>() ?? 0,
                                ContentTxt = contentTxt,
                                SourceKind = "☁️ Supabase Cloud"
                            });
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // 2. Mezclar con el caché local y archivos .txt en la carpeta ARCA_SharedParameters
            var localRecords = LoadFromLocalCache();
            foreach (var loc in localRecords)
            {
                if (!results.Any(r => r.ProfileTitle.Equals(loc.ProfileTitle, StringComparison.OrdinalIgnoreCase) && r.ParamCount == loc.ParamCount))
                {
                    results.Add(loc);
                }
            }

            // 3. Asegurar que siempre exista el perfil semilla "ARCA Estándar General BIM"
            if (results.Count == 0)
            {
                string defaultTxt = BuildDefaultArcaTxtContent();
                results.Add(new CloudSharedParamFileRecord
                {
                    Id = "seed-arca-default",
                    ProfileTitle = "Estándar General ARCA BIM (Oficial)",
                    FileName = "ARCA_Estandar_BIM.txt",
                    ProjectName = "Estándar Corporativo",
                    AuthorEmail = "bim@arcareview.com",
                    UpdatedAt = DateTime.Now,
                    GroupCount = 4,
                    ParamCount = 9,
                    ContentTxt = defaultTxt,
                    SourceKind = "🏛️ Plantilla Integrada"
                });
            }

            return results.OrderByDescending(r => r.UpdatedAt).ToList();
        }

        /// <summary>
        /// Descarga/materializa un registro de la nube en disco local y opcionalmente lo activa en Revit.
        /// </summary>
        public static string MaterializeCloudRecordToLocalFile(Application app, CloudSharedParamFileRecord record, bool setAsActiveInRevit)
        {
            string safeName = string.Join("_", (record.FileName ?? "ARCA_SharedParams.txt").Split(Path.GetInvalidFileNameChars()));
            if (!safeName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                safeName += ".txt";

            string localPath = Path.Combine(LocalLibraryFolder, safeName);
            File.WriteAllText(localPath, record.ContentTxt, Encoding.UTF8);

            if (setAsActiveInRevit)
            {
                app.SharedParametersFilename = localPath;
            }

            return localPath;
        }

        /// <summary>
        /// Fusiona los parámetros de un perfil Cloud hacia el archivo .txt local activo sin sobrescribir GUIDs existentes.
        /// </summary>
        public static int MergeCloudRecordIntoActiveTxt(Application app, Document? doc, CloudSharedParamFileRecord cloudRecord)
        {
            string localTxtPath = EnsureDefaultSharedParamFile(app, true);
            string tempCloudFile = Path.Combine(Path.GetTempPath(), $"arca_cloud_merge_{Guid.NewGuid():N}.txt");

            try
            {
                File.WriteAllText(tempCloudFile, cloudRecord.ContentTxt, Encoding.UTF8);
                var localItems = ReadSharedParameterFileItems(app, doc, localTxtPath);
                var cloudItems = ReadSharedParameterFileItems(app, doc, tempCloudFile);

                var localGuids = new HashSet<Guid>(localItems.Where(x => x.Guid != Guid.Empty).Select(x => x.Guid));
                var localNames = new HashSet<string>(localItems.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);

                int addedCount = 0;
                var groupedMissing = cloudItems
                    .Where(c => c.Guid != Guid.Empty && !localGuids.Contains(c.Guid) && !localNames.Contains(c.Name))
                    .GroupBy(c => c.GroupName);

                foreach (var grp in groupedMissing)
                {
                    var listToAdd = grp.Select(x => (x.Guid, x.Name, x.DataTypeLabel)).ToList();
                    if (listToAdd.Count > 0)
                    {
                        AppendParametersWithKnownGuidsToTxtFile(localTxtPath, grp.Key, listToAdd);
                        addedCount += listToAdd.Count;
                    }
                }

                app.SharedParametersFilename = localTxtPath;
                return addedCount;
            }
            finally
            {
                try { if (File.Exists(tempCloudFile)) File.Delete(tempCloudFile); } catch { }
            }
        }

        private static void SaveToLocalCache(CloudSharedParamFileRecord record)
        {
            try
            {
                var list = LoadFromLocalCache();
                list.RemoveAll(x => x.FileName.Equals(record.FileName, StringComparison.OrdinalIgnoreCase) &&
                                    x.ProfileTitle.Equals(record.ProfileTitle, StringComparison.OrdinalIgnoreCase));
                record.SourceKind = "💾 Caché Local + Cloud";
                list.Insert(0, record);
                File.WriteAllText(LocalIndexJsonPath, JsonConvert.SerializeObject(list, Formatting.Indented), Encoding.UTF8);
            }
            catch { }
        }

        private static List<CloudSharedParamFileRecord> LoadFromLocalCache()
        {
            try
            {
                if (File.Exists(LocalIndexJsonPath))
                {
                    string json = File.ReadAllText(LocalIndexJsonPath, Encoding.UTF8);
                    return JsonConvert.DeserializeObject<List<CloudSharedParamFileRecord>>(json) ?? new List<CloudSharedParamFileRecord>();
                }
            }
            catch { }
            return new List<CloudSharedParamFileRecord>();
        }
    }
}
