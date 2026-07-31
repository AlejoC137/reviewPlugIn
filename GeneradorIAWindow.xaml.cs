using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitToSupabasePlugin
{
    public partial class GeneradorIAWindow : System.Windows.Window
    {
        private Document _doc;

        public GeneradorIAWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc;

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

        private void CopyPrompt_Click(object sender, RoutedEventArgs e)
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

            string prompt = $@"Eres un experto modelador BIM 3D e Ingeniero de Diseño Paramétrico especializado en la reconstrucción tridimensional de familias para Autodesk Revit.

Tu tarea crítica es generar una reconstrucción geométrica LO MÁS FIDEDIGNA Y DETALLADA POSIBLE basada en la imagen o manual técnico adjunto. Analiza minuciosamente las proporciones, alineaciones, escalas relativas y características morfológicas, utilizando absolutamente TODA la información visual disponible.

Debes traducir la máquina o equipo a un ensamblaje paramétrico tridimensional descomponiendo la geometría de forma sistemática en tres niveles de detalle:

* Macro-geometría: Chasis principal, bastidor, gabinete exterior y estructuras envolventes.
* Meso-geometría: Paneles inclinados, cubiertas, biseles, tolvas, puertas, marcos, ductos y viseras.
* Micro-geometría: Patas niveladoras, botones, perillas, pantallas, rejillas de ventilación, bisagras, conectores, dispensadores y relieves.

Devuélveme ÚNICAMENTE un bloque JSON válido (sin código Python ni comentarios) que cumpla ESTRICTAMENTE con este esquema:json
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

```

Reglas Obligatorias:
1. Todas las medidas en la geometría deben estar en MILÍMETROS matemáticamente calculados desde un origen de coordenadas absoluto (0,0,0).
2. Puedes usar `type: ""box""`, `""cylinder""` y `""custom_extrusion""`.
3. Para `box`, DEBES proveer `x_min`, `x_max`, `y_min`, `y_max`, `z_min`, `z_max`.
4. Para `cylinder`, el `axis` puede ser `""X""`, `""Y""` o `""Z""`.
   - Si `axis = ""Z""`: provee `center_x`, `center_y`, `radius`, `z_min`, `z_max`.
   - Si `axis = ""Y""`: provee `center_x`, `center_z`, `radius`, `y_min`, `y_max`.
   - Si `axis = ""X""`: provee `center_y`, `center_z`, `radius`, `x_min`, `x_max`.
5. Para `custom_extrusion`, el `axis` define el plano de dibujo 2D (X, Y o Z) y la dirección de la extrusión. `start` y `end` definen el inicio y fin de la extrusión. El `profile` es una secuencia continua de puntos 2D `[u, v]` que dibuja la cara lateral (úsalo OBLIGATORIAMENTE para capturar paneles inclinados, diagonales, biseles, chaflanes o formas complejas asimétricas uniéndolas punto por punto):
   - Si axis = ""X"", U = Y y V = Z (extrusión a lo largo del eje X).
   - Si axis = ""Y"", U = Z y V = X (extrusión a lo largo del eje Y).
   - Si axis = ""Z"", U = X y V = Y (extrusión a lo largo del eje Z).
6. {lodInstruction}
7. Devuelve SOLO el bloque JSON envuelto en ```json ... ```. Prohibido escribir código Python o explicaciones de texto antes o después del JSON.

```";

            try
            {
                System.Windows.Clipboard.SetText(prompt);
                txtStatus.Text = "✅ Prompt copiado al portapapeles. ¡Pégalo en Gemini!";
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
