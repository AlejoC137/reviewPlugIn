using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ArcaReviewInstaller
{
    public partial class MainWindow : Window
    {
        private readonly string[] _knownVersions = new[] { "2024", "2025", "2026", "2027" };

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Log("Bienvenido al instalador de ARCA REVIEW.");
            CheckRevitRunning();
            DetectInstalledRevitVersions();
        }

        private bool CheckRevitRunning()
        {
            var processes = Process.GetProcessesByName("Revit");
            bool isRunning = processes.Length > 0;
            RevitWarningBorder.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
            if (isRunning)
            {
                Log("⚠️ ADVERTENCIA: Se detectó Autodesk Revit en ejecución.");
            }
            return isRunning;
        }

        private void CheckRevitProcess_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckRevitRunning())
            {
                Log("✅ Revit ya no está en ejecución. Listo para proceder.");
            }
        }

        private void DetectInstalledRevitVersions()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string baseAddins = Path.Combine(appData, "Autodesk", "Revit", "Addins");

            var detected = new List<string>();

            if (Directory.Exists(baseAddins))
            {
                foreach (var ver in _knownVersions)
                {
                    string verPath = Path.Combine(baseAddins, ver);
                    if (Directory.Exists(verPath))
                    {
                        detected.Add(ver);
                    }
                }
            }

            // Actualizar checks
            Chk2024.IsChecked = detected.Contains("2024");
            Chk2025.IsChecked = detected.Contains("2025") || (!detected.Any() && true);
            Chk2026.IsChecked = detected.Contains("2026");
            Chk2027.IsChecked = detected.Contains("2027");

            if (detected.Any())
            {
                DetectionNoticeText.Text = $"Versiones de Revit detectadas en tu equipo: {string.Join(", ", detected)}";
                Log($"Carpetas encontradas para: {string.Join(", ", detected)}");
            }
            else
            {
                DetectionNoticeText.Text = "No se detectaron carpetas previas; se creará la carpeta para las versiones seleccionadas.";
                Log("No se encontraron carpetas previas en AppData. Se crearán al instalar.");
            }
        }

        private List<string> GetSelectedVersions()
        {
            var list = new List<string>();
            if (Chk2024.IsChecked == true) list.Add("2024");
            if (Chk2025.IsChecked == true) list.Add("2025");
            if (Chk2026.IsChecked == true) list.Add("2026");
            if (Chk2027.IsChecked == true) list.Add("2027");
            return list;
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            var versions = GetSelectedVersions();
            if (!versions.Any())
            {
                MessageBox.Show("Por favor selecciona al menos una versión de Revit para instalar el plugin.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (CheckRevitRunning())
            {
                var result = MessageBox.Show(
                    "Autodesk Revit está abierto actualmente.\n\nPara evitar que los archivos queden bloqueados o no se actualicen, es altamente recomendable cerrar Revit antes de continuar.\n\n¿Deseas continuar de todas maneras?",
                    "Revit en ejecución",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            SetBusyState(true);
            InstallProgressBar.Value = 10;
            Log($"Iniciando instalación para Revit {string.Join(", ", versions)}...");

            try
            {
                await Task.Run(() => PerformInstallation(versions));
                InstallProgressBar.Value = 100;
                Log("🎉 ¡INSTALACIÓN COMPLETADA EXITOSAMENTE!");
                MessageBox.Show("¡ARCA REVIEW se ha instalado correctamente!\nYa puedes abrir Revit para comenzar a utilizarlo.", "Instalación Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                InstallProgressBar.Value = 0;
                Log($"❌ ERROR durante la instalación: {ex.Message}");
                MessageBox.Show($"Ocurrió un error al instalar:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusyState(false);
            }
        }

        private void PerformInstallation(List<string> versions)
        {
            byte[]? zipBytes = LoadPayloadBytes();
            if (zipBytes == null || zipBytes.Length == 0)
            {
                throw new FileNotFoundException("No se encontró el paquete de archivos (payload.zip) dentro del instalador.");
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string baseAddins = Path.Combine(appData, "Autodesk", "Revit", "Addins");

            foreach (var ver in versions)
            {
                string targetDir = Path.Combine(baseAddins, ver);
                Dispatcher.Invoke(() => Log($"Instalando en Revit {ver} ({targetDir})..."));

                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                using var ms = new MemoryStream(zipBytes);
                using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

                int totalEntries = archive.Entries.Count;
                int current = 0;

                foreach (var entry in archive.Entries)
                {
                    current++;
                    string destinationPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));

                    // Evitar Zip Slip vulnerability
                    if (!destinationPath.StartsWith(Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        // Es un directorio
                        Directory.CreateDirectory(destinationPath);
                        continue;
                    }

                    string? parentDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    // Manejar reemplazo de archivos bloqueados
                    if (File.Exists(destinationPath))
                    {
                        try
                        {
                            File.Delete(destinationPath);
                        }
                        catch
                        {
                            // Si está bloqueado, intentar renombrar
                            string oldPath = destinationPath + ".old";
                            try
                            {
                                if (File.Exists(oldPath)) File.Delete(oldPath);
                                File.Move(destinationPath, oldPath);
                            }
                            catch
                            {
                                // Si aún falla, arrojar excepción comprensible
                                throw new IOException($"El archivo '{Path.GetFileName(destinationPath)}' está siendo usado por Revit u otro proceso. Por favor cierra Revit e intenta de nuevo.");
                            }
                        }
                    }

                    entry.ExtractToFile(destinationPath, true);

                    int pct = (int)((double)current / totalEntries * 80) + 10;
                    Dispatcher.Invoke(() =>
                    {
                        InstallProgressBar.Value = pct;
                    });
                }

                Dispatcher.Invoke(() => Log($"✅ Archivos instalados para Revit {ver}."));
            }
        }

        private byte[]? LoadPayloadBytes()
        {
            var asm = Assembly.GetExecutingAssembly();
            
            // 1. Intentar desde recurso incrustado
            string resourceName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase)) ?? "";
            if (!string.IsNullOrEmpty(resourceName))
            {
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
            }

            // 2. Fallback de desarrollo: archivo local Resources/payload.zip
            string localFallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "payload.zip");
            if (File.Exists(localFallback))
            {
                return File.ReadAllBytes(localFallback);
            }

            return null;
        }

        private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            var versions = GetSelectedVersions();
            if (!versions.Any())
            {
                MessageBox.Show("Por favor selecciona qué versión(es) de Revit deseas desinstalar.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"¿Estás seguro de que deseas desinstalar ARCA REVIEW de Revit {string.Join(", ", versions)}?",
                "Confirmar Desinstalación",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            SetBusyState(true);
            Log("Desinstalando ARCA REVIEW...");

            try
            {
                await Task.Run(() =>
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string baseAddins = Path.Combine(appData, "Autodesk", "Revit", "Addins");

                    foreach (var ver in versions)
                    {
                        string targetDir = Path.Combine(baseAddins, ver);
                        if (!Directory.Exists(targetDir)) continue;

                        Dispatcher.Invoke(() => Log($"Limpiando Revit {ver}..."));

                        // Archivos principales a eliminar
                        string addinFile = Path.Combine(targetDir, "RevitToSupabasePlugin.addin");
                        if (File.Exists(addinFile)) File.Delete(addinFile);

                        string dllFile = Path.Combine(targetDir, "RevitToSupabasePlugin.dll");
                        if (File.Exists(dllFile))
                        {
                            try { File.Delete(dllFile); }
                            catch { File.Move(dllFile, dllFile + ".old"); }
                        }

                        string depsFile = Path.Combine(targetDir, "RevitToSupabasePlugin.deps.json");
                        if (File.Exists(depsFile)) File.Delete(depsFile);

                        string jsonNet = Path.Combine(targetDir, "Newtonsoft.Json.dll");
                        if (File.Exists(jsonNet))
                        {
                            try { File.Delete(jsonNet); } catch { }
                        }

                        // Eliminar carpetas de activos creadas por el plugin
                        string familiasDir = Path.Combine(targetDir, "Familias");
                        if (Directory.Exists(familiasDir))
                        {
                            try { Directory.Delete(familiasDir, true); } catch { }
                        }

                        string familiasRiegoDir = Path.Combine(targetDir, "FamiliasRiego");
                        if (Directory.Exists(familiasRiegoDir))
                        {
                            try { Directory.Delete(familiasRiegoDir, true); } catch { }
                        }

                        string ladrilloDir = Path.Combine(targetDir, "LadrilloProfile");
                        if (Directory.Exists(ladrilloDir))
                        {
                            try { Directory.Delete(ladrilloDir, true); } catch { }
                        }

                        Dispatcher.Invoke(() => Log($"✅ ARCA REVIEW desinstalado de Revit {ver}."));
                    }
                });

                InstallProgressBar.Value = 0;
                MessageBox.Show("ARCA REVIEW se ha desinstalado de las versiones seleccionadas.", "Desinstalación Completa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"❌ Error al desinstalar: {ex.Message}");
                MessageBox.Show($"Ocurrió un error al desinstalar:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusyState(false);
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var versions = GetSelectedVersions();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string path = Path.Combine(appData, "Autodesk", "Revit", "Addins");

            if (versions.Count == 1)
            {
                string specific = Path.Combine(path, versions[0]);
                if (Directory.Exists(specific)) path = specific;
            }

            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show($"La carpeta no existe todavía:\n{path}", "Carpeta no encontrada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SetBusyState(bool busy)
        {
            BtnInstall.IsEnabled = !busy;
            BtnUninstall.IsEnabled = !busy;
            BtnOpenFolder.IsEnabled = !busy;
            Chk2024.IsEnabled = !busy;
            Chk2025.IsEnabled = !busy;
            Chk2026.IsEnabled = !busy;
            Chk2027.IsEnabled = !busy;
        }

        private void Log(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            LogTextBox.AppendText(line + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        }
    }
}
