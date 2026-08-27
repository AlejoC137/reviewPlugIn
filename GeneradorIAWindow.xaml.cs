using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    public partial class GeneradorIAWindow : System.Windows.Window
    {
        private Document _doc;
        private static readonly HttpClient _httpClient = new HttpClient();
        private string? _selectedMediaPath = null;

        public GeneradorIAWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc;

            btnGenerateLive.Click += GenerateLive_Click;
            btnSelectMedia.Click += SelectMedia_Click;
            btnClearMedia.Click += ClearMedia_Click;
            btnCopyPrompt.Click += CopyPrompt_Click;
            btnPaste.Click += Paste_Click;
            btnBuild.Click += Build_Click;
        }

        private double MmToFt(double mm)
        {
            return mm / 304.8;
        }

        private string? ExtractJson(string text)
        {
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start != -1 && end != -1 && end > start)
            {
                return text.Substring(start, end - start + 1);
            }
            return null;
        }

        private string GetSystemInstructions()
        {
            var selectedLOD = cmbLOD.SelectedItem as ComboBoxItem;
            string lodText = selectedLOD?.Content?.ToString() ?? "";

            var selectedLOI = cmbLOI.SelectedItem as ComboBoxItem;
            string loiText = selectedLOI?.Content?.ToString() ?? "";

            string lodInstruction = "Desglosa la máquina en al menos 5-10 piezas clave usando cajas y cilindros.";
            if (lodText.Contains("LOD 200"))
            {
                lodInstruction = "Mantén la geometría lo más simple posible (1 a 3 cajas como máximo) para un bloque conceptual general.";
            }
            else if (lodText.Contains("LOD 350"))
            {
                lodInstruction = "ÉNFASIS CRÍTICO: Construye el modelo LO MÁS FIDEDIGNO Y DETALLADO POSIBLE. Usa TODA la información visual. Desglosa la máquina en docenas de piezas (patas individuales, cuerpo, tolvas, paneles frontales, botones, dispensadores, rejillas). No escatimes en añadir componentes adicionales para igualar la realidad.";
            }

            string loiInstruction = "{\"name\": \"Peso_kg\", \"type\": \"Number\", \"value\": 100.0}";
            if (loiText.Contains("Técnico"))
            {
                loiInstruction = "{\"name\": \"Voltaje\", \"type\": \"Text\", \"value\": \"110V\"}, {\"name\": \"Refrigerante\", \"type\": \"Text\", \"value\": \"R404a\"}";
            }
            else if (loiText.Contains("Mantenimiento"))
            {
                loiInstruction = "{\"name\": \"Garantia_Meses\", \"type\": \"Number\", \"value\": 12.0}, {\"name\": \"Fecha_Fabricacion\", \"type\": \"Text\", \"value\": \"2024\"}";
            }

            return $@"Eres un experto modelador BIM 3D e Ingeniero de Diseño Paramétrico especializado en la reconstrucción tridimensional de familias para Autodesk Revit.

Tu tarea crítica es generar una reconstrucción geométrica LO MÁS FIDEDIGNA Y DETALLADA POSIBLE basada en la descripción e imágenes/frames solicitados. Analiza minuciosamente las proporciones, alineaciones, escalas relativas y características morfológicas.

Debes traducir la máquina o equipo a un ensamblaje paramétrico tridimensional descomponiendo la geometría de forma sistemática en tres niveles de detalle:
* Macro-geometría: Chasis principal, bastidor, gabinete exterior y estructuras envolventes.
* Meso-geometría: Paneles inclinados, cubiertas, biseles, tolvas, puertas, marcos, ductos y viseras.
* Micro-geometría: Patas niveladoras, botones, perillas, pantallas, rejillas de ventilación, bisagras, conectores, dispensadores y relieves.

Devuélveme ÚNICAMENTE un bloque JSON válido (sin código Python ni comentarios) que cumpla ESTRICTAMENTE con este esquema:

{{
  ""parameters"": [
    {{""name"": ""Modelo"", ""type"": ""Text"", ""value"": ""Texto_A_Extraer""}},
    {loiInstruction}
  ],
  ""geometry"": [
    {{
      ""type"": ""box"",
      ""name"": ""Cuerpo Principal - Gabinete Base"",
      ""x_min"": -270, ""x_max"": 270,
      ""y_min"": -355, ""y_max"": 355,
      ""z_min"": 50, ""z_max"": 860
    }},
    {{
      ""type"": ""cylinder"",
      ""name"": ""Pata delantera"",
      ""axis"": ""Z"",
      ""center_x"": -200, ""center_y"": -300,
      ""radius"": 25,
      ""z_min"": 0, ""z_max"": 50
    }},
    {{
      ""type"": ""custom_extrusion"",
      ""name"": ""Panel Frontal Diagonal"",
      ""axis"": ""X"",
      ""profile"": [
        [0, 50],
        [270, 50],
        [270, 400],
        [150, 450],
        [0, 450]
      ],
      ""start"": -355,
      ""end"": -200
    }}
  ]
}}

Reglas Obligatorias:
1. Todas las medidas en la geometría deben estar en MILÍMETROS matemáticamente calculados desde un origen de coordenadas absoluto (0,0,0).
2. Puedes usar `type: ""box""`, `""cylinder""` y `""custom_extrusion""`.
3. Para `box`, DEBES proveer `x_min`, `x_max`, `y_min`, `y_max`, `z_min`, `z_max`.
4. Para `cylinder`, el `axis` puede ser `""X""`, `""Y""` o `""Z""`.
5. Para `custom_extrusion`, el `axis` define el plano de dibujo 2D (X, Y o Z). `start` y `end` definen el inicio y fin de la extrusión.
6. {lodInstruction}
7. Devuelve SOLO el bloque JSON. Prohibido escribir código Python o explicaciones de texto antes o después del JSON.";
        }

        private void SelectMedia_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "Todos los Formatos (*.png;*.jpg;*.pdf;*.ifc;*.dwg;*.dxf;*.mp4)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.pdf;*.ifc;*.ifcjson;*.dwg;*.dxf;*.mp4;*.mov;*.avi|Documentos y Modelos (*.pdf;*.ifc;*.dwg)|*.pdf;*.ifc;*.ifcjson;*.dwg;*.dxf|Imágenes (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Videos (*.mp4;*.mov)|*.mp4;*.mov;*.avi";

            if (openFileDialog.ShowDialog() == true)
            {
                _selectedMediaPath = openFileDialog.FileName;
                lblMediaStatus.Text = Path.GetFileName(_selectedMediaPath);
                btnClearMedia.Visibility = System.Windows.Visibility.Visible;

                string ext = Path.GetExtension(_selectedMediaPath).ToLower();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".webp")
                {
                    try
                    {
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(_selectedMediaPath);
                        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmap.EndInit();

                        imgPreview.Source = bitmap;
                        imgPreview.Visibility = System.Windows.Visibility.Visible;
                        txtMediaIcon.Visibility = System.Windows.Visibility.Collapsed;
                    }
                    catch
                    {
                        imgPreview.Visibility = System.Windows.Visibility.Collapsed;
                        txtMediaIcon.Visibility = System.Windows.Visibility.Visible;
                        txtMediaIcon.Text = "📷 Imagen";
                    }
                }
                else if (ext == ".pdf")
                {
                    imgPreview.Visibility = System.Windows.Visibility.Collapsed;
                    txtMediaIcon.Visibility = System.Windows.Visibility.Visible;
                    txtMediaIcon.Text = "📄 PDF";
                }
                else if (ext == ".ifc" || ext == ".ifcjson")
                {
                    imgPreview.Visibility = System.Windows.Visibility.Collapsed;
                    txtMediaIcon.Visibility = System.Windows.Visibility.Visible;
                    txtMediaIcon.Text = "🏗️ IFC";
                }
                else if (ext == ".dwg" || ext == ".dxf")
                {
                    imgPreview.Visibility = System.Windows.Visibility.Collapsed;
                    txtMediaIcon.Visibility = System.Windows.Visibility.Visible;
                    txtMediaIcon.Text = "📐 DWG";
                }
                else
                {
                    imgPreview.Visibility = System.Windows.Visibility.Collapsed;
                    txtMediaIcon.Visibility = System.Windows.Visibility.Visible;
                    txtMediaIcon.Text = "🎥 Video";
                }
            }
        }

        private void ClearMedia_Click(object sender, RoutedEventArgs e)
        {
            _selectedMediaPath = null;
            lblMediaStatus.Text = "Sin archivo adjunto";
            btnClearMedia.Visibility = System.Windows.Visibility.Collapsed;
            imgPreview.Source = null;
            imgPreview.Visibility = System.Windows.Visibility.Collapsed;
            txtMediaIcon.Visibility = System.Windows.Visibility.Visible;
            txtMediaIcon.Text = "📄/📐";
        }

        private async Task<(List<string> base64Images, string extraText)> ProcessAttachedFileAsync(string filePath)
        {
            List<string> images = new List<string>();
            string textData = "";
            string ext = Path.GetExtension(filePath).ToLower();

            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".webp")
            {
                byte[] bytes = await File.ReadAllBytesAsync(filePath);
                string mime = ext == ".png" ? "image/png" : (ext == ".webp" ? "image/webp" : "image/jpeg");
                images.Add($"data:{mime};base64,{Convert.ToBase64String(bytes)}");
            }
            else if (ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".wmv" || ext == ".mkv")
            {
                List<string> videoFrames = await ExtractFramesFromVideoAsync(filePath, 3);
                images.AddRange(videoFrames);
            }
            else if (ext == ".pdf")
            {
                textData = await ProcessPdfFileAsync(filePath);
            }
            else if (ext == ".ifc" || ext == ".ifcjson")
            {
                textData = await ProcessIfcFileAsync(filePath);
            }
            else if (ext == ".dwg" || ext == ".dxf")
            {
                textData = await ProcessDwgFileAsync(filePath);
            }

            return (images, textData);
        }
        private async Task<string> ProcessPdfFileAsync(string pdfPath)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\n[INFORMACIÓN Y TEXTO EXTRAÍDO DEL DOCUMENTO PDF ADJUNTO]");
            sb.AppendLine($"Nombre de archivo: {Path.GetFileName(pdfPath)}");

            try
            {
                byte[] bytes = await File.ReadAllBytesAsync(pdfPath);
                string rawText = Encoding.ASCII.GetString(bytes);

                var mediaBoxMatch = System.Text.RegularExpressions.Regex.Match(rawText, @"/MediaBox\s*\[\s*([0-9\.\-]+)\s+([0-9\.\-]+)\s+([0-9\.\-]+)\s+([0-9\.\-]+)\s*\]");
                if (mediaBoxMatch.Success)
                {
                    sb.AppendLine($"Dimensiones de página PDF: {mediaBoxMatch.Groups[3].Value} x {mediaBoxMatch.Groups[4].Value} pt");
                }

                List<string> textSnippets = new List<string>();
                var matches = System.Text.RegularExpressions.Regex.Matches(rawText, @"\(([^()]{3,100})\)\s*Tj");
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    string val = m.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(val) && !textSnippets.Contains(val) && textSnippets.Count < 30)
                    {
                        textSnippets.Add(val);
                    }
                }

                if (textSnippets.Count == 0)
                {
                    var arrayMatches = System.Text.RegularExpressions.Regex.Matches(rawText, @"\[\s*\(([^()]{2,100})\)");
                    foreach (System.Text.RegularExpressions.Match m in arrayMatches)
                    {
                        string val = m.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(val) && !textSnippets.Contains(val) && textSnippets.Count < 30)
                        {
                            textSnippets.Add(val);
                        }
                    }
                }

                if (textSnippets.Count > 0)
                {
                    sb.AppendLine("Textos y especificaciones extraídas del PDF:");
                    foreach (var t in textSnippets) sb.AppendLine(" - " + t);
                }
                else
                {
                    sb.AppendLine("Documento PDF analizado (Metadatos y estructura extraída).");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("Error al leer el archivo PDF: " + ex.Message);
            }

            return sb.ToString();
        }

        private async Task<string> ProcessIfcFileAsync(string ifcPath)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\n[DESCRIPCIÓN Y ESTRUCTURA EXTRAÍDA DEL ARCHIVO IFC ADJUNTO]");
            sb.AppendLine($"Nombre de archivo IFC: {Path.GetFileName(ifcPath)}");

            try
            {
                string content = await File.ReadAllTextAsync(ifcPath);
                var lines = content.Split('\n');

                List<string> entities = new List<string>();
                List<string> properties = new List<string>();

                foreach (var l in lines)
                {
                    string trimmed = l.Trim();
                    if (trimmed.StartsWith("IFC", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("= IFC", StringComparison.OrdinalIgnoreCase))
                    {
                        if (entities.Count < 25)
                        {
                            entities.Add(trimmed);
                        }
                    }
                    if (trimmed.Contains("IFCPROPERTY", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("IFCVALUEMEASURE", StringComparison.OrdinalIgnoreCase))
                    {
                        if (properties.Count < 20)
                        {
                            properties.Add(trimmed);
                        }
                    }
                }

                sb.AppendLine("Entidades del modelo IFC:");
                foreach (var e in entities) sb.AppendLine(" - " + e);
                sb.AppendLine("Propiedades del modelo IFC:");
                foreach (var p in properties) sb.AppendLine(" - " + p);
            }
            catch (Exception ex)
            {
                sb.AppendLine("Error al leer el archivo IFC: " + ex.Message);
            }

            return sb.ToString();
        }

        private async Task<string> ProcessDwgFileAsync(string dwgPath)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\n[DESCRIPCIÓN Y ESTRUCTURA EXTRAÍDA DEL PLANO DWG/DXF ADJUNTO]");
            sb.AppendLine($"Nombre de archivo CAD: {Path.GetFileName(dwgPath)}");

            try
            {
                string ext = Path.GetExtension(dwgPath).ToLower();
                if (ext == ".dxf")
                {
                    string text = await File.ReadAllTextAsync(dwgPath);
                    var lines = text.Split('\n');
                    List<string> layers = new List<string>();
                    List<string> texts = new List<string>();

                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i].Trim();
                        if (line == "8" && i + 1 < lines.Length)
                        {
                            string layerName = lines[i + 1].Trim();
                            if (!layers.Contains(layerName) && layers.Count < 15)
                                layers.Add(layerName);
                        }
                        if ((line == "TEXT" || line == "MTEXT") && i + 2 < lines.Length)
                        {
                            string val = lines[i + 2].Trim();
                            if (val.Length > 1 && !texts.Contains(val) && texts.Count < 20)
                                texts.Add(val);
                        }
                    }

                    sb.AppendLine("Capas del archivo CAD:");
                    foreach (var l in layers) sb.AppendLine(" - Capa: " + l);
                    sb.AppendLine("Textos y cotas del plano CAD:");
                    foreach (var t in texts) sb.AppendLine(" - Texto: " + t);
                }
                else
                {
                    byte[] rawBytes = await File.ReadAllBytesAsync(dwgPath);
                    List<string> stringsFound = new List<string>();
                    StringBuilder currentStr = new StringBuilder();

                    foreach (byte b in rawBytes)
                    {
                        if (b >= 32 && b <= 126)
                        {
                            currentStr.Append((char)b);
                        }
                        else
                        {
                            if (currentStr.Length >= 4)
                            {
                                string s = currentStr.ToString();
                                if (!s.StartsWith("AC10") && !s.Contains("AutoCAD") && stringsFound.Count < 20)
                                {
                                    stringsFound.Add(s);
                                }
                            }
                            currentStr.Clear();
                        }
                    }

                    sb.AppendLine("Metadatos y textos extraídos del DWG:");
                    foreach (var s in stringsFound) sb.AppendLine(" - " + s);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("Error al procesar archivo DWG: " + ex.Message);
            }

            return sb.ToString();
        }

        private Task<List<string>> ExtractFramesFromVideoAsync(string videoPath, int maxFrames)
        {
            var tcs = new TaskCompletionSource<List<string>>();

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    List<string> frames = new List<string>();
                    var mediaPlayer = new System.Windows.Media.MediaPlayer();
                    mediaPlayer.ScrubbingEnabled = true;

                    mediaPlayer.MediaOpened += (s, e) =>
                    {
                        try
                        {
                            double totalSeconds = mediaPlayer.NaturalDuration.HasTimeSpan ? mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds : 2.0;
                            int videoWidth = mediaPlayer.NaturalVideoWidth > 0 ? mediaPlayer.NaturalVideoWidth : 640;
                            int videoHeight = mediaPlayer.NaturalVideoHeight > 0 ? mediaPlayer.NaturalVideoHeight : 480;

                            double[] samplePoints = totalSeconds > 1.0 ? new double[] { 0.2, totalSeconds / 2.0, totalSeconds * 0.8 } : new double[] { 0.1 };

                            foreach (double sec in samplePoints)
                            {
                                mediaPlayer.Position = TimeSpan.FromSeconds(sec);

                                var visual = new System.Windows.Media.DrawingVisual();
                                using (var dc = visual.RenderOpen())
                                {
                                    dc.DrawVideo(mediaPlayer, new Rect(0, 0, videoWidth, videoHeight));
                                }

                                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(videoWidth, videoHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                                rtb.Render(visual);

                                var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));

                                using (MemoryStream ms = new MemoryStream())
                                {
                                    encoder.Save(ms);
                                    byte[] frameBytes = ms.ToArray();
                                    frames.Add($"data:image/jpeg;base64,{Convert.ToBase64String(frameBytes)}");
                                }
                            }

                            mediaPlayer.Close();
                            tcs.SetResult(frames);
                        }
                        catch (Exception ex)
                        {
                            mediaPlayer.Close();
                            tcs.SetException(ex);
                        }
                    };

                    mediaPlayer.MediaFailed += (s, e) =>
                    {
                        mediaPlayer.Close();
                        tcs.SetException(new Exception("Error al cargar video: " + e.ErrorException?.Message));
                    };

                    mediaPlayer.Open(new Uri(videoPath));
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return tcs.Task;
        }

        private async void GenerateLive_Click(object sender, RoutedEventArgs e)
        {
            string apiKey = txtApiKey.Text.Trim();
            string userPrompt = txtUserPrompt.Text.Trim();

            if (string.IsNullOrEmpty(apiKey))
            {
                System.Windows.MessageBox.Show("Por favor ingresa tu API Key.", "API Key Requerida", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(userPrompt) && string.IsNullOrEmpty(_selectedMediaPath))
            {
                System.Windows.MessageBox.Show("Por favor escribe una descripción o adjunta un archivo (PDF, IFC, DWG, Foto o Video).", "Entrada Requerida", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnGenerateLive.IsEnabled = false;
            txtStatus.Text = "⏳ Procesando archivo adjunto...";

            try
            {
                string selectedModel = "gpt-4o-mini";
                string apiEndpoint = "https://api.openai.com/v1/chat/completions";

                var itemModel = cmbModel.SelectedItem as ComboBoxItem;
                string modelNameStr = itemModel?.Content?.ToString() ?? "";

                if (modelNameStr.Contains("gpt-4o-mini"))
                {
                    selectedModel = "gpt-4o-mini";
                    apiEndpoint = "https://api.openai.com/v1/chat/completions";
                }
                else if (modelNameStr.Contains("gpt-4o"))
                {
                    selectedModel = "gpt-4o";
                    apiEndpoint = "https://api.openai.com/v1/chat/completions";
                }
                else if (modelNameStr.Contains("gemini"))
                {
                    selectedModel = "gemini-2.0-flash";
                    apiEndpoint = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";
                }

                List<string> mediaBase64List = new List<string>();
                string extraDocText = "";

                if (!string.IsNullOrEmpty(_selectedMediaPath))
                {
                    txtStatus.Text = "📷 Analizando y procesando archivo adjunto...";
                    var processed = await ProcessAttachedFileAsync(_selectedMediaPath);
                    mediaBase64List = processed.base64Images;
                    extraDocText = processed.extraText;
                }

                string systemPrompt = GetSystemInstructions();

                string fullPromptText = userPrompt;
                if (!string.IsNullOrEmpty(extraDocText))
                {
                    fullPromptText += "\n" + extraDocText;
                }

                object userMessageContent;
                if (mediaBase64List.Count > 0)
                {
                    List<object> contentList = new List<object>();
                    contentList.Add(new { type = "text", text = $"Genera la geometría en JSON para la siguiente familia o máquina analizando el archivo adjunto: {fullPromptText}" });

                    foreach (var base64Img in mediaBase64List)
                    {
                        contentList.Add(new
                        {
                            type = "image_url",
                            image_url = new { url = base64Img }
                        });
                    }
                    userMessageContent = contentList;
                }
                else
                {
                    userMessageContent = $"Genera la geometría en JSON para la siguiente familia o máquina: {fullPromptText}";
                }

                var payload = new
                {
                    model = selectedModel,
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userMessageContent }
                    },
                    response_format = new { type = "json_object" },
                    temperature = 0.2
                };

                txtStatus.Text = $"⏳ Enviando a Agente IA ({selectedModel})...";
                string jsonBody = JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                using (var req = new HttpRequestMessage(HttpMethod.Post, apiEndpoint))
                {
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                    req.Content = content;

                    HttpResponseMessage response = await _httpClient.SendAsync(req);
                    string responseString = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        txtStatus.Text = "❌ Error en API Key o servidor IA (" + response.StatusCode + ")";
                        System.Windows.MessageBox.Show($"Error al conectar con la API ({response.StatusCode}):\n{responseString}", "Error de Conexión IA");
                        return;
                    }

                    JObject resObj = JObject.Parse(responseString);
                    string? aiContent = resObj["choices"]?[0]?["message"]?["content"]?.ToString();

                    if (!string.IsNullOrEmpty(aiContent))
                    {
                        string? cleanJson = ExtractJson(aiContent);
                        txtJson.Text = cleanJson ?? aiContent;
                        txtStatus.Text = "✅ ¡Geometría 3D generada desde multimedia! Revisa y haz clic en Construir.";
                    }
                    else
                    {
                        txtStatus.Text = "⚠️ La IA devolvió una respuesta vacía.";
                    }
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = "❌ Error al comunicarse con la IA.";
                System.Windows.MessageBox.Show("Ocurrió un error en el procesamiento:\n" + ex.Message, "Error Multi-Modal");
            }
            finally
            {
                btnGenerateLive.IsEnabled = true;
            }
        }

        private void CopyPrompt_Click(object sender, RoutedEventArgs e)
        {
            string prompt = GetSystemInstructions();
            try
            {
                System.Windows.Clipboard.SetText(prompt);
                txtStatus.Text = "✅ Prompt copiado al portapapeles.";
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Error al copiar: " + ex.Message;
            }
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string text = System.Windows.Clipboard.GetText();
                string? clean = ExtractJson(text);
                if (!string.IsNullOrEmpty(clean))
                {
                    txtJson.Text = clean;
                    txtStatus.Text = "✅ JSON pegado. ¡Listo para construir!";
                }
                else
                {
                    txtStatus.Text = "⚠️ No hay JSON válido en el portapapeles.";
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Error al pegar: " + ex.Message;
            }
        }

        private void Build_Click(object sender, RoutedEventArgs e)
        {
            if (!_doc.IsFamilyDocument)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Generador IA", "Debes abrir un entorno de Familia (.rfa) en Revit para usar este generador.");
                return;
            }

            string jsonStr = txtJson.Text.Trim();
            if (string.IsNullOrEmpty(jsonStr))
            {
                Autodesk.Revit.UI.TaskDialog.Show("Generador IA", "El cuadro de JSON está vacío.");
                return;
            }

            JObject data;
            try
            {
                data = JObject.Parse(jsonStr);
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Error de JSON", "Error de sintaxis JSON:\n" + ex.Message);
                return;
            }

            this.Hide();

            using (Transaction t = new Transaction(_doc, "Generar Familia por JSON IA"))
            {
                t.Start();

                try
                {
                    // 1. Limpiar geometría anterior no bloqueada
                    var collector = new FilteredElementCollector(_doc)
                                        .OfClass(typeof(GenericForm));
                    foreach (GenericForm form in collector)
                    {
                        if (!form.Pinned)
                        {
                            try { _doc.Delete(form.Id); } catch { }
                        }
                    }

                    // 2. Parámetros de Familia
                    FamilyManager famMan = _doc.FamilyManager;
                    JArray? parameters = data["parameters"] as JArray;
                    if (parameters != null)
                    {
                        foreach (var p in parameters)
                        {
                            string? pName = p["name"]?.ToString();
                            if (string.IsNullOrEmpty(pName)) continue;

                            string pTypeStr = p["type"]?.ToString() ?? "Text";
                            var pVal = p["value"];

                            bool isText = pTypeStr.Equals("text", StringComparison.OrdinalIgnoreCase) ||
                                         pTypeStr.Equals("string", StringComparison.OrdinalIgnoreCase);

                            FamilyParameter? param = famMan.get_Parameter(pName);
                            if (param == null)
                            {
                                try
                                {
                                    ForgeTypeId groupTypeId = GroupTypeId.IdentityData;
                                    ForgeTypeId specTypeId = isText ? SpecTypeId.String.Text : SpecTypeId.Number;
                                    param = famMan.AddParameter(pName, groupTypeId, specTypeId, false);
                                }
                                catch
                                {
                                    continue;
                                }
                            }

                            if (param != null)
                            {
                                try
                                {
                                    if (isText)
                                    {
                                        famMan.Set(param, pVal?.ToString() ?? "");
                                    }
                                    else
                                    {
                                        famMan.Set(param, Convert.ToDouble(pVal));
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    // 3. Geometría
                    Plane planeZ = Plane.CreateByOriginAndBasis(XYZ.Zero, XYZ.BasisX, XYZ.BasisY);
                    SketchPlane spZ = SketchPlane.Create(_doc, planeZ);

                    Plane planeX = Plane.CreateByOriginAndBasis(XYZ.Zero, XYZ.BasisY, XYZ.BasisZ);
                    SketchPlane spX = SketchPlane.Create(_doc, planeX);

                    Plane planeY = Plane.CreateByOriginAndBasis(XYZ.Zero, XYZ.BasisZ, XYZ.BasisX);
                    SketchPlane spY = SketchPlane.Create(_doc, planeY);

                    JArray? geometry = data["geometry"] as JArray;
                    if (geometry != null)
                    {
                        foreach (var g in geometry)
                        {
                            string gType = g["type"]?.ToString()?.ToLower() ?? "";

                            if (gType == "box")
                            {
                                double xmin = MmToFt(Convert.ToDouble(g["x_min"]));
                                double xmax = MmToFt(Convert.ToDouble(g["x_max"]));
                                double ymin = MmToFt(Convert.ToDouble(g["y_min"]));
                                double ymax = MmToFt(Convert.ToDouble(g["y_max"]));
                                double zmin = MmToFt(Convert.ToDouble(g["z_min"]));
                                double zmax = MmToFt(Convert.ToDouble(g["z_max"]));

                                double temp;
                                if (xmin > xmax) { temp = xmin; xmin = xmax; xmax = temp; }
                                if (ymin > ymax) { temp = ymin; ymin = ymax; ymax = temp; }
                                if (zmin > zmax) { temp = zmin; zmin = zmax; zmax = temp; }

                                if (xmax - xmin < 0.01) xmax = xmin + 0.1;
                                if (ymax - ymin < 0.01) ymax = ymin + 0.1;
                                if (zmax - zmin < 0.01) zmax = zmin + 0.1;

                                CurveArray ca = new CurveArray();
                                ca.Append(Line.CreateBound(new XYZ(xmin, ymin, 0), new XYZ(xmax, ymin, 0)));
                                ca.Append(Line.CreateBound(new XYZ(xmax, ymin, 0), new XYZ(xmax, ymax, 0)));
                                ca.Append(Line.CreateBound(new XYZ(xmax, ymax, 0), new XYZ(xmin, ymax, 0)));
                                ca.Append(Line.CreateBound(new XYZ(xmin, ymax, 0), new XYZ(xmin, ymin, 0)));

                                CurveArrArray carr = new CurveArrArray();
                                carr.Append(ca);

                                Extrusion ext = _doc.FamilyCreate.NewExtrusion(true, carr, spZ, 1.0);
                                ext.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM).Set(zmin);
                                ext.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM).Set(zmax);
                            }
                            else if (gType == "cylinder")
                            {
                                string axis = g["axis"]?.ToString()?.ToUpper() ?? "Z";
                                double r = MmToFt(Convert.ToDouble(g["radius"] ?? 10));
                                if (r < 0.01) r = 0.1;

                                if (axis == "X")
                                {
                                    double cy = MmToFt(Convert.ToDouble(g["center_y"] ?? 0));
                                    double cz = MmToFt(Convert.ToDouble(g["center_z"] ?? 0));
                                    double xmin = MmToFt(Convert.ToDouble(g["x_min"] ?? 0));
                                    double xmax = MmToFt(Convert.ToDouble(g["x_max"] ?? 0));
                                    if (xmin > xmax) { double tempVal = xmin; xmin = xmax; xmax = tempVal; }
                                    if (xmax - xmin < 0.01) xmax = xmin + 0.1;

                                    XYZ center = new XYZ(0, cy, cz);
                                    Arc arc1 = Arc.Create(center, r, 0, Math.PI, XYZ.BasisY, XYZ.BasisZ);
                                    Arc arc2 = Arc.Create(center, r, Math.PI, 2 * Math.PI, XYZ.BasisY, XYZ.BasisZ);

                                    CurveArray ca = new CurveArray();
                                    ca.Append(arc1);
                                    ca.Append(arc2);

                                    CurveArrArray carr = new CurveArrArray();
                                    carr.Append(ca);

                                    Extrusion ext = _doc.FamilyCreate.NewExtrusion(true, carr, spX, 1.0);
                                    ext.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM).Set(xmin);
                                    ext.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM).Set(xmax);
                                }
                                else if (axis == "Y")
                                {
                                    double cx = MmToFt(Convert.ToDouble(g["center_x"] ?? 0));
                                    double cz = MmToFt(Convert.ToDouble(g["center_z"] ?? 0));
                                    double ymin = MmToFt(Convert.ToDouble(g["y_min"] ?? 0));
                                    double ymax = MmToFt(Convert.ToDouble(g["y_max"] ?? 0));
                                    if (ymin > ymax) { double tempVal = ymin; ymin = ymax; ymax = tempVal; }
                                    if (ymax - ymin < 0.01) ymax = ymin + 0.1;

                                    XYZ center = new XYZ(cx, 0, cz);
                                    Arc arc1 = Arc.Create(center, r, 0, Math.PI, XYZ.BasisZ, XYZ.BasisX);
                                    Arc arc2 = Arc.Create(center, r, Math.PI, 2 * Math.PI, XYZ.BasisZ, XYZ.BasisX);

                                    CurveArray ca = new CurveArray();
                                    ca.Append(arc1);
                                    ca.Append(arc2);

                                    CurveArrArray carr = new CurveArrArray();
                                    carr.Append(ca);

                                    Extrusion ext = _doc.FamilyCreate.NewExtrusion(true, carr, spY, 1.0);
                                    ext.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM).Set(ymin);
                                    ext.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM).Set(ymax);
                                }
                                else // Z axis
                                {
                                    double cx = MmToFt(Convert.ToDouble(g["center_x"] ?? 0));
                                    double cy = MmToFt(Convert.ToDouble(g["center_y"] ?? 0));
                                    double zmin = MmToFt(Convert.ToDouble(g["z_min"] ?? 0));
                                    double zmax = MmToFt(Convert.ToDouble(g["z_max"] ?? 0));
                                    if (zmin > zmax) { double tempVal = zmin; zmin = zmax; zmax = tempVal; }
                                    if (zmax - zmin < 0.01) zmax = zmin + 0.1;

                                    XYZ center = new XYZ(cx, cy, 0);
                                    Arc arc1 = Arc.Create(center, r, 0, Math.PI, XYZ.BasisX, XYZ.BasisY);
                                    Arc arc2 = Arc.Create(center, r, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY);

                                    CurveArray ca = new CurveArray();
                                    ca.Append(arc1);
                                    ca.Append(arc2);

                                    CurveArrArray carr = new CurveArrArray();
                                    carr.Append(ca);

                                    Extrusion ext = _doc.FamilyCreate.NewExtrusion(true, carr, spZ, 1.0);
                                    ext.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM).Set(zmin);
                                    ext.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM).Set(zmax);
                                }
                            }
                            else if (gType == "custom_extrusion")
                            {
                                string axis = g["axis"]?.ToString()?.ToUpper() ?? "Z";
                                double startVal = MmToFt(Convert.ToDouble(g["start"] ?? 0));
                                double endVal = MmToFt(Convert.ToDouble(g["end"] ?? 0));

                                if (startVal > endVal) { double tempVal = startVal; startVal = endVal; endVal = tempVal; }
                                if (endVal - startVal < 0.01) endVal = startVal + 0.1;

                                JArray? profilePts = g["profile"] as JArray;
                                if (profilePts == null || profilePts.Count < 3) continue;

                                List<XYZ> pts = new List<XYZ>();
                                foreach (var ptToken in profilePts)
                                {
                                    var coords = ptToken as JArray;
                                    if (coords != null && coords.Count >= 2)
                                    {
                                        double u = MmToFt(Convert.ToDouble(coords[0]));
                                        double v = MmToFt(Convert.ToDouble(coords[1]));
                                        pts.Add(new XYZ(u, v, 0));
                                    }
                                }

                                if (pts.Count < 3) continue;

                                // Close profile if needed
                                XYZ p0 = pts[0];
                                XYZ pLast = pts[pts.Count - 1];
                                if (Math.Abs(p0.X - pLast.X) > 0.001 || Math.Abs(p0.Y - pLast.Y) > 0.001)
                                {
                                    pts.Add(p0);
                                }

                                CurveArray ca = new CurveArray();
                                SketchPlane sp;

                                for (int i = 0; i < pts.Count - 1; i++)
                                {
                                    double u1 = pts[i].X;
                                    double v1 = pts[i].Y;
                                    double u2 = pts[i + 1].X;
                                    double v2 = pts[i + 1].Y;

                                    if (Math.Abs(u1 - u2) < 0.0001 && Math.Abs(v1 - v2) < 0.0001)
                                        continue;

                                    XYZ pt1, pt2;
                                    if (axis == "X")
                                    {
                                        pt1 = new XYZ(0, u1, v1);
                                        pt2 = new XYZ(0, u2, v2);
                                        sp = spX;
                                    }
                                    else if (axis == "Y")
                                    {
                                        pt1 = new XYZ(v1, 0, u1);
                                        pt2 = new XYZ(v2, 0, u2);
                                        sp = spY;
                                    }
                                    else
                                    {
                                        pt1 = new XYZ(u1, v1, 0);
                                        pt2 = new XYZ(u2, v2, 0);
                                        sp = spZ;
                                    }

                                    ca.Append(Line.CreateBound(pt1, pt2));
                                }

                                if (ca.Size < 3) continue;

                                CurveArrArray carr = new CurveArrArray();
                                carr.Append(ca);

                                SketchPlane finalSp = spZ;
                                if (axis == "X") finalSp = spX;
                                else if (axis == "Y") finalSp = spY;

                                Extrusion ext = _doc.FamilyCreate.NewExtrusion(true, carr, finalSp, 1.0);
                                ext.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM).Set(startVal);
                                ext.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM).Set(endVal);
                            }
                        }
                    }

                    t.Commit();
                    this.Close();
                    Autodesk.Revit.UI.TaskDialog.Show("Generador IA", "✅ Modelo generado desde JSON con éxito.");
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    this.Show();
                    Autodesk.Revit.UI.TaskDialog.Show("Error", "⚠️ Error al generar la geometría:\n" + ex.Message + "\n" + ex.StackTrace);
                }
            }
        }
    }
}
