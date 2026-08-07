using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Collections.Generic;

namespace RevitToSupabasePlugin
{
    public partial class ManualAppWindow : Window
    {
        private const string JSON_CONTEXT_PROMPT = @"{
  ""application"": {
    ""name"": ""RevitToSupabasePlugin / reviewPlugIn"",
    ""ribbon_tab"": ""📕 Review"",
    ""target_framework"": ""net10.0-windows / Revit 2027+"",
    ""architecture"": ""ExternalApplication + WPF + ExternalEvents + Supabase REST API""
  },
  ""modules"": [
    {
      ""id"": ""MOD-ALBA"",
      ""name"": ""Módulo de Albañilería y Familias de Ladrillos 3D"",
      ""commands"": [""CommandGenerarFamiliasLadrillos"", ""CommandConvertirMurosALadrillos""],
      ""ui"": [""ManagerFamiliasLadrilloWindow.xaml"", ""ConvertirMurosWindow.xaml""],
      ""key_methodologies"": [
        ""Pre-corte en Perfil 2D para Mortero Pega 3D"",
        ""DirectShapeType para habilitar Edit Type en Revit"",
        ""Autocentrado por BoundingBoxXYZ al eje del muro"",
        ""Parseo numérico multicultura safe""
      ]
    },
    {
      ""id"": ""MOD-RIEGO"",
      ""name"": ""Módulo de Sistemas de Riego e Instalaciones"",
      ""commands"": [""CommandCargarPlantillaRiego""],
      ""ui"": [""ConfiguradorRiegoWindow.xaml""],
      ""key_methodologies"": [
        ""Mapeo dinámico de cotizaciones JSON catalog_mapping a categorías MEP"",
        ""Generación procedural de tuberías PE 16mm y fittings (Tees/Codos)"",
        ""Asignación de caudales LPH/GPH a aspersores y nebulizadores""
      ]
    },
    {
      ""id"": ""MOD-META"",
      ""name"": ""Módulo Exportador de Metadata, IFC/JSON y Escena Web 3D"",
      ""commands"": [""Command""],
      ""ui"": [""ExportadorMetadataWindow.xaml""],
      ""services"": [""MetadataExporterService.cs"", ""IfcJsonExporterService.cs""],
      ""key_methodologies"": [
        ""Recolección eficiente con FilteredElementCollector"",
        ""Exportación de mallas geométricas Web3DSceneData para Three.js/Xeokit""
      ]
    },
    {
      ""id"": ""MOD-IA-PLANTILLA"",
      ""name"": ""Módulo Configurador IA de Plantillas y Schedules"",
      ""commands"": [""CommandConfiguradorPlantilla"", ""CommandSchedulesIA""],
      ""ui"": [""ConfiguradorIAPlantillaWindow.xaml"", ""SchedulesIAWindow.xaml""],
      ""services"": [""PlantillaApplicatorService.cs""]
    },
    {
      ""id"": ""MOD-SUPABASE"",
      ""name"": ""Módulo Backend & Sync Supabase Cloud"",
      ""services"": [""SupabaseService.cs"", ""SupabaseAuthService.cs""]
    }
  ]
}";

        public ManualAppWindow()
        {
            InitializeComponent();
            txtJsonContext.Text = JSON_CONTEXT_PROMPT;
            txtFullCodePreview.Text = GetCombinedCodebaseText();
        }

        private void BtnCopyContext_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetText(JSON_CONTEXT_PROMPT);
                SetStatus("✅ ¡Contexto JSON copiado al portapapeles!", System.Windows.Media.Brushes.LightGreen);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("No se pudo copiar el contexto: " + ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void BtnCopyDocs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string docsText = GetDocumentationText();
                if (!string.IsNullOrEmpty(docsText))
                {
                    System.Windows.Clipboard.SetText(docsText);
                    SetStatus("✅ ¡Documentación completa (.md) copiada al portapapeles!", System.Windows.Media.Brushes.LightGreen);
                }
                else
                {
                    System.Windows.MessageBox.Show("No se encontró el archivo de documentación .md.", "Info", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error al copiar la documentación: " + ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void BtnCopyCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string combinedCode = txtFullCodePreview.Text;
                if (string.IsNullOrEmpty(combinedCode))
                {
                    combinedCode = GetCombinedCodebaseText();
                }

                System.Windows.Clipboard.SetText(combinedCode);
                SetStatus("✅ ¡Todo el código fuente C#/XAML copiado al portapapeles! Listo para pegar en IA.", System.Windows.Media.Brushes.LightGreen);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error al copiar el código fuente: " + ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void BtnExportZip_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string zipPath = Path.Combine(desktopPath, "reviewPlugIn_SourceCode.zip");

                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

                string projectDir = GetProjectDirectory();
                CreateCleanZip(projectDir, zipPath);

                SetStatus($"📦 ZIP creado en el Escritorio: {Path.GetFileName(zipPath)}", System.Windows.Media.Brushes.LightGreen);

                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{zipPath}\"");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error al generar el archivo ZIP: " + ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void BtnOpenMarkdown_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string brainDir = Path.Combine(userDir, ".gemini", "antigravity", "brain");

                if (Directory.Exists(brainDir))
                {
                    string[] mdFiles = Directory.GetFiles(brainDir, "*.md", SearchOption.AllDirectories);
                    if (mdFiles.Length > 0)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = mdFiles[0],
                            UseShellExecute = true
                        });
                        SetStatus("📄 Documentación abierta en el sistema.", System.Windows.Media.Brushes.LightBlue);
                        return;
                    }
                }

                string localReadme = Path.Combine(GetProjectDirectory(), "README_USO.md");
                if (File.Exists(localReadme))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = localReadme,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error al abrir la documentación: " + ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void SetStatus(string msg, System.Windows.Media.Brush color)
        {
            lblStatus.Text = msg;
            lblStatus.Foreground = color;
        }

        private string GetProjectDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // Si estamos ejecutando desde bin/Debug/net10.0-windows, subir a la raíz del proyecto
            DirectoryInfo? info = new DirectoryInfo(baseDir);
            while (info != null && !File.Exists(Path.Combine(info.FullName, "RevitToSupabasePlugin.csproj")))
            {
                info = info.Parent;
            }
            return info?.FullName ?? baseDir;
        }

        private string GetDocumentationText()
        {
            string userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string brainDir = Path.Combine(userDir, ".gemini", "antigravity", "brain");

            if (Directory.Exists(brainDir))
            {
                string[] mdFiles = Directory.GetFiles(brainDir, "*.md", SearchOption.AllDirectories);
                if (mdFiles.Length > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (string file in mdFiles)
                    {
                        sb.AppendLine($"<!-- FILE: {Path.GetFileName(file)} -->");
                        sb.AppendLine(File.ReadAllText(file));
                        sb.AppendLine("\n---\n");
                    }
                    return sb.ToString();
                }
            }

            string localReadme = Path.Combine(GetProjectDirectory(), "README_USO.md");
            if (File.Exists(localReadme))
            {
                return File.ReadAllText(localReadme);
            }

            return "";
        }

        private string GetCombinedCodebaseText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("// =========================================================================");
            sb.AppendLine("// PROYECTO COMPLETO: reviewPlugIn / RevitToSupabasePlugin");
            sb.AppendLine("// Unificado para análisis de Inteligencia Artificial");
            sb.AppendLine("// =========================================================================\n");

            string projDir = GetProjectDirectory();
            if (!Directory.Exists(projDir)) return sb.ToString();

            string[] validExtensions = new[] { ".cs", ".xaml", ".csproj", ".addin", ".bat", ".json", ".txt" };

            foreach (string filePath in Directory.GetFiles(projDir, "*.*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(projDir, filePath);
                if (relativePath.StartsWith("bin") || relativePath.StartsWith("obj") || relativePath.StartsWith(".git") || relativePath.StartsWith(".vs"))
                    continue;

                string ext = Path.GetExtension(filePath).ToLower();
                if (Array.Exists(validExtensions, e => e == ext))
                {
                    try
                    {
                        sb.AppendLine($"// =========================================================================");
                        sb.AppendLine($"// ARCHIVO: {relativePath}");
                        sb.AppendLine($"// =========================================================================");
                        sb.AppendLine(File.ReadAllText(filePath));
                        sb.AppendLine();
                    }
                    catch { }
                }
            }

            return sb.ToString();
        }

        private void CreateCleanZip(string sourceDir, string destinationZip)
        {
            using (ZipArchive archive = ZipFile.Open(destinationZip, ZipArchiveMode.Create))
            {
                foreach (string file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
                {
                    string relativePath = Path.GetRelativePath(sourceDir, file);
                    if (relativePath.StartsWith("bin") || relativePath.StartsWith("obj") || relativePath.StartsWith(".git") || relativePath.StartsWith(".vs"))
                        continue;

                    archive.CreateEntryFromFile(file, relativePath, CompressionLevel.Optimal);
                }
            }
        }
    }
}
