using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconGenerator
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string current = AppDomain.CurrentDomain.BaseDirectory;
            DirectoryInfo? dir = new DirectoryInfo(current);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RevitToSupabasePlugin.csproj")))
            {
                dir = dir.Parent;
            }
            string outputDir = dir != null ? dir.FullName : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\"));
            Console.WriteLine($"Generando iconos B&W Review en: {outputDir}");

            var icons = new (string name, Action<DrawingContext, double> draw)[]
            {
                ("icon_control_panel", DrawControlPanel),
                ("icon_sync_sheets", DrawSyncSheets),
                ("icon_gen_ia", DrawGenIA),
                ("icon_export_metadata", DrawExportMetadata),
                ("icon_config_plantilla", DrawConfigPlantilla),
                ("icon_tablas_ia", DrawTablasIA),
                ("icon_sync_materials", DrawSyncMaterials),
                ("icon_qaqc", DrawQAQC),
                ("icon_riego_plantilla", DrawRiegoPlantilla),
                ("icon_plumbing_sheet", DrawPlumbingSheet),
                ("icon_gen_ladrillos", DrawGenLadrillos),
                ("icon_conv_muros", DrawConvMuros),
                ("icon_tablas_ladrillos", DrawTablasLadrillos),
                ("icon_gen_pisos", DrawGenPisos),
                ("icon_conv_pisos", DrawConvPisos),
                ("icon_tablas_pisos", DrawTablasPisos),
                ("icon_gestor_cat", DrawGestorCategorias),
                ("icon_gen_steel", DrawGenSteel),
                ("icon_conv_steel", DrawConvSteel),
                ("icon_tablas_steel", DrawTablasSteel),
                ("icon_plan_ambientador", DrawPlanAmbientador),
                ("icon_detalles", DrawDetalles),
                ("icon_manual", DrawManual),
                // Fallbacks para compatibilidad
                ("icon_copilot", DrawGenIA),
                ("icon_generador", DrawGenIA),
                ("icon_costos", DrawTablasLadrillos),
                ("icon_doc", DrawSyncSheets),
                ("icon_masivo", DrawExportMetadata)
            };

            foreach (var icon in icons)
            {
                // 16x16 para Revit Ribbon Small/Stacked
                string p16 = Path.Combine(outputDir, $"{icon.name}.png");
                RenderAndSave(icon.draw, 16, p16);

                // 32x32 para Revit Ribbon Large
                string p32 = Path.Combine(outputDir, $"{icon.name}_32.png");
                RenderAndSave(icon.draw, 32, p32);

                // Copiar también a la carpeta tools para mantener coherencia
                string toolsDir = Path.Combine(outputDir, "tools");
                if (Directory.Exists(toolsDir))
                {
                    try
                    {
                        File.Copy(p16, Path.Combine(toolsDir, $"{icon.name}.png"), true);
                        File.Copy(p32, Path.Combine(toolsDir, $"{icon.name}_32.png"), true);
                    }
                    catch { }
                }

                Console.WriteLine($"Icono B&W generado: {icon.name}");
            }

            Console.WriteLine("¡Todos los iconos B&W del sistema Review fueron generados exitosamente!");
        }

        static void RenderAndSave(Action<DrawingContext, double> drawAction, int size, string filePath)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                drawAction(dc, size);
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                encoder.Save(stream);
            }
        }

        // =========================================================================
        // ARCA REVIEW B&W DESIGN SYSTEM PALETTE
        // =========================================================================
        static readonly SolidColorBrush Ink = new SolidColorBrush(Color.FromRgb(28, 28, 25));       // #1C1C19 Pure Dark Ink
        static readonly SolidColorBrush White = new SolidColorBrush(Color.FromRgb(255, 255, 255));   // #FFFFFF Crisp White
        static readonly SolidColorBrush LightTint = new SolidColorBrush(Color.FromRgb(246, 243, 238)); // #F6F3EE Paper Tint

        static Pen P(double thickness, bool round = true)
        {
            var pen = new Pen(Ink, thickness)
            {
                StartLineCap = round ? PenLineCap.Round : PenLineCap.Square,
                EndLineCap = round ? PenLineCap.Round : PenLineCap.Square,
                LineJoin = round ? PenLineJoin.Round : PenLineJoin.Miter
            };
            pen.Freeze();
            return pen;
        }

        static Pen WhitePen(double thickness)
        {
            var pen = new Pen(White, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();
            return pen;
        }

        // -------------------------------------------------------------------------
        // 0. Panel de Control (Consola con sliders técnicos)
        // -------------------------------------------------------------------------
        static void DrawControlPanel(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.3 * sc;

            // Marco del panel
            dc.DrawRoundedRectangle(White, P(w), new Rect(1.5 * sc, 1.5 * sc, 13 * sc, 13 * sc), 1.5 * sc, 1.5 * sc);

            // Slider 1
            dc.DrawLine(P(1.0 * sc), new Point(4 * sc, 5 * sc), new Point(12 * sc, 5 * sc));
            dc.DrawEllipse(Ink, null, new Point(6 * sc, 5 * sc), 1.6 * sc, 1.6 * sc);

            // Slider 2
            dc.DrawLine(P(1.0 * sc), new Point(4 * sc, 8 * sc), new Point(12 * sc, 8 * sc));
            dc.DrawEllipse(Ink, null, new Point(10 * sc, 8 * sc), 1.6 * sc, 1.6 * sc);

            // Slider 3
            dc.DrawLine(P(1.0 * sc), new Point(4 * sc, 11 * sc), new Point(12 * sc, 11 * sc));
            dc.DrawEllipse(Ink, null, new Point(7.5 * sc, 11 * sc), 1.6 * sc, 1.6 * sc);
        }

        // -------------------------------------------------------------------------
        // 1. Cola de Sheets (Lámina técnica con flechas de sincronización)
        // -------------------------------------------------------------------------
        static void DrawSyncSheets(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.3 * sc;

            // Lámina / Plano con margen técnico
            dc.DrawRectangle(White, P(w), new Rect(2 * sc, 1.5 * sc, 12 * sc, 13 * sc));
            dc.DrawRectangle(null, P(0.8 * sc), new Rect(3.5 * sc, 3 * sc, 9 * sc, 10 * sc));

            // Rótulo título plano (esquina inferior)
            dc.DrawRectangle(Ink, null, new Rect(8 * sc, 10.5 * sc, 4.5 * sc, 2.5 * sc));

            // Flechas de sincronización en el centro
            var syncArc = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(5 * sc, 7.5 * sc), IsClosed = false };
            fig.Segments.Add(new ArcSegment(new Point(10 * sc, 7.5 * sc), new Size(2.8 * sc, 2.8 * sc), 0, false, SweepDirection.Clockwise, true));
            syncArc.Figures.Add(fig);
            dc.DrawGeometry(null, P(1.2 * sc), syncArc);

            // Punta flecha
            dc.DrawLine(P(1.2 * sc), new Point(10 * sc, 7.5 * sc), new Point(8.8 * sc, 6.2 * sc));
        }

        // -------------------------------------------------------------------------
        // 2. Generador Familias IA (Cubo isométrico wireframe con destello ✦)
        // -------------------------------------------------------------------------
        static void DrawGenIA(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.3 * sc;

            // Cubo isométrico - Cara superior
            var top = new PathGeometry();
            var fTop = new PathFigure { StartPoint = new Point(8 * sc, 1.8 * sc), IsClosed = true };
            fTop.Segments.Add(new LineSegment(new Point(13.5 * sc, 4.8 * sc), true));
            fTop.Segments.Add(new LineSegment(new Point(8 * sc, 7.8 * sc), true));
            fTop.Segments.Add(new LineSegment(new Point(2.5 * sc, 4.8 * sc), true));
            top.Figures.Add(fTop);
            dc.DrawGeometry(White, P(w), top);

            // Cara izquierda
            var left = new PathGeometry();
            var fLeft = new PathFigure { StartPoint = new Point(2.5 * sc, 4.8 * sc), IsClosed = true };
            fLeft.Segments.Add(new LineSegment(new Point(8 * sc, 7.8 * sc), true));
            fLeft.Segments.Add(new LineSegment(new Point(8 * sc, 14.2 * sc), true));
            fLeft.Segments.Add(new LineSegment(new Point(2.5 * sc, 11.2 * sc), true));
            left.Figures.Add(fLeft);
            dc.DrawGeometry(LightTint, P(w), left);

            // Cara derecha
            var right = new PathGeometry();
            var fRight = new PathFigure { StartPoint = new Point(8 * sc, 7.8 * sc), IsClosed = true };
            fRight.Segments.Add(new LineSegment(new Point(13.5 * sc, 4.8 * sc), true));
            fRight.Segments.Add(new LineSegment(new Point(13.5 * sc, 11.2 * sc), true));
            fRight.Segments.Add(new LineSegment(new Point(8 * sc, 14.2 * sc), true));
            right.Figures.Add(fRight);
            dc.DrawGeometry(White, P(w), right);

            // Destello IA ✦ (4 puntas sólido)
            DrawSparkle(dc, 12 * sc, 3.5 * sc, 2.4 * sc);
        }

        // -------------------------------------------------------------------------
        // 3. Exportar Metadata (Discos de base de datos con flecha exportar)
        // -------------------------------------------------------------------------
        static void DrawExportMetadata(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.3 * sc;

            // Cilindro Base de Datos - Nivel Superior
            dc.DrawEllipse(White, P(w), new Point(6 * sc, 4 * sc), 4.5 * sc, 1.8 * sc);

            // Nivel medio
            var mid = new PathGeometry();
            var fMid = new PathFigure { StartPoint = new Point(1.5 * sc, 4 * sc), IsClosed = false };
            fMid.Segments.Add(new LineSegment(new Point(1.5 * sc, 8.5 * sc), true));
            fMid.Segments.Add(new ArcSegment(new Point(10.5 * sc, 8.5 * sc), new Size(4.5 * sc, 1.8 * sc), 0, false, SweepDirection.Counterclockwise, true));
            fMid.Segments.Add(new LineSegment(new Point(10.5 * sc, 4 * sc), true));
            mid.Figures.Add(fMid);
            dc.DrawGeometry(White, P(w), mid);

            // Nivel inferior
            var bot = new PathGeometry();
            var fBot = new PathFigure { StartPoint = new Point(1.5 * sc, 8.5 * sc), IsClosed = false };
            fBot.Segments.Add(new LineSegment(new Point(1.5 * sc, 13 * sc), true));
            fBot.Segments.Add(new ArcSegment(new Point(10.5 * sc, 13 * sc), new Size(4.5 * sc, 1.8 * sc), 0, false, SweepDirection.Counterclockwise, true));
            fBot.Segments.Add(new LineSegment(new Point(10.5 * sc, 8.5 * sc), true));
            bot.Figures.Add(fBot);
            dc.DrawGeometry(White, P(w), bot);

            // Flecha salida limpia (hacia la derecha)
            dc.DrawLine(P(1.8 * sc), new Point(9 * sc, 8.5 * sc), new Point(15 * sc, 8.5 * sc));
            dc.DrawLine(P(1.8 * sc), new Point(12.5 * sc, 6 * sc), new Point(15 * sc, 8.5 * sc));
            dc.DrawLine(P(1.8 * sc), new Point(12.5 * sc, 11 * sc), new Point(15 * sc, 8.5 * sc));
        }

        // -------------------------------------------------------------------------
        // 4. Configurador Plantillas (Hoja plegada con engranaje de configuración)
        // -------------------------------------------------------------------------
        static void DrawConfigPlantilla(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.3 * sc;

            // Hoja doblada
            var sheet = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(2.5 * sc, 1.5 * sc), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(8.5 * sc, 1.5 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(12.5 * sc, 5.5 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(12.5 * sc, 14.5 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(2.5 * sc, 14.5 * sc), true));
            sheet.Figures.Add(fig);
            dc.DrawGeometry(White, P(w), sheet);

            // Doblez esquina
            var fold = new PathGeometry();
            var fFold = new PathFigure { StartPoint = new Point(8.5 * sc, 1.5 * sc), IsClosed = true };
            fFold.Segments.Add(new LineSegment(new Point(8.5 * sc, 5.5 * sc), true));
            fFold.Segments.Add(new LineSegment(new Point(12.5 * sc, 5.5 * sc), true));
            fold.Figures.Add(fFold);
            dc.DrawGeometry(LightTint, P(0.9 * sc), fold);

            // Líneas de plantilla
            dc.DrawLine(P(1.0 * sc), new Point(4.5 * sc, 5 * sc), new Point(7.5 * sc, 5 * sc));
            dc.DrawLine(P(1.0 * sc), new Point(4.5 * sc, 8 * sc), new Point(9 * sc, 8 * sc));
            dc.DrawLine(P(1.0 * sc), new Point(4.5 * sc, 11 * sc), new Point(7.5 * sc, 11 * sc));

            // Engranaje técnico en esquina inferior derecha
            dc.DrawEllipse(White, P(1.2 * sc), new Point(11.5 * sc, 11.5 * sc), 3.2 * sc, 3.2 * sc);
            dc.DrawEllipse(Ink, null, new Point(11.5 * sc, 11.5 * sc), 1.2 * sc, 1.2 * sc);
        }

        // -------------------------------------------------------------------------
        // 5. Asistente Tablas IA (Tabla de cómputo con cuadrícula y ✦)
        // -------------------------------------------------------------------------
        static void DrawTablasIA(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.3 * sc;

            // Marco tabla
            dc.DrawRectangle(White, P(w), new Rect(1.5 * sc, 2 * sc, 13 * sc, 12 * sc));

            // Fila de encabezado
            dc.DrawRectangle(LightTint, P(0.9 * sc), new Rect(1.5 * sc, 2 * sc, 13 * sc, 3.5 * sc));

            // Líneas de datos
            dc.DrawLine(P(0.9 * sc), new Point(1.5 * sc, 8.5 * sc), new Point(14.5 * sc, 8.5 * sc));
            dc.DrawLine(P(0.9 * sc), new Point(1.5 * sc, 11.5 * sc), new Point(14.5 * sc, 11.5 * sc));

            // Columnas
            dc.DrawLine(P(0.9 * sc), new Point(6.5 * sc, 2 * sc), new Point(6.5 * sc, 14 * sc));
            dc.DrawLine(P(0.9 * sc), new Point(10.5 * sc, 5.5 * sc), new Point(10.5 * sc, 14 * sc));

            // Destello IA ✦ en encabezado
            DrawSparkle(dc, 12.5 * sc, 3.7 * sc, 1.8 * sc);
        }

        // -------------------------------------------------------------------------
        // 6. Sincronizar Materiales (Muestra circular con flechas cíclicas)
        // -------------------------------------------------------------------------
        static void DrawSyncMaterials(DrawingContext dc, double s)
        {
            double sc = s / 16.0;

            // Esfera de material en centro
            dc.DrawEllipse(White, P(1.4 * sc), new Point(8 * sc, 8 * sc), 3.8 * sc, 3.8 * sc);
            // Sombra interna rayada/media luna para dar volumen arquitectónico
            var halfArc = new PathGeometry();
            var fHalf = new PathFigure { StartPoint = new Point(8 * sc, 4.2 * sc), IsClosed = false };
            fHalf.Segments.Add(new ArcSegment(new Point(8 * sc, 11.8 * sc), new Size(3.8 * sc, 3.8 * sc), 0, false, SweepDirection.Clockwise, true));
            halfArc.Figures.Add(fHalf);
            dc.DrawGeometry(Ink, null, halfArc);

            // Flecha de sincronización superior
            var topArc = new PathGeometry();
            var fTop = new PathFigure { StartPoint = new Point(3 * sc, 8 * sc), IsClosed = false };
            fTop.Segments.Add(new ArcSegment(new Point(12 * sc, 4 * sc), new Size(6 * sc, 6 * sc), 0, false, SweepDirection.Clockwise, true));
            topArc.Figures.Add(fTop);
            dc.DrawGeometry(null, P(1.2 * sc), topArc);
            dc.DrawLine(P(1.2 * sc), new Point(12 * sc, 4 * sc), new Point(10 * sc, 3.5 * sc));
            dc.DrawLine(P(1.2 * sc), new Point(12 * sc, 4 * sc), new Point(12 * sc, 6 * sc));

            // Flecha de sincronización inferior
            var botArc = new PathGeometry();
            var fBot = new PathFigure { StartPoint = new Point(13 * sc, 8 * sc), IsClosed = false };
            fBot.Segments.Add(new ArcSegment(new Point(4 * sc, 12 * sc), new Size(6 * sc, 6 * sc), 0, false, SweepDirection.Clockwise, true));
            botArc.Figures.Add(fBot);
            dc.DrawGeometry(null, P(1.2 * sc), botArc);
            dc.DrawLine(P(1.2 * sc), new Point(4 * sc, 12 * sc), new Point(6 * sc, 12.5 * sc));
            dc.DrawLine(P(1.2 * sc), new Point(4 * sc, 12 * sc), new Point(4 * sc, 10 * sc));
        }

        // -------------------------------------------------------------------------
        // 7. Bi-MCP Status / QAQC (Escudo técnico con checkmark ✔)
        // -------------------------------------------------------------------------
        static void DrawQAQC(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.3 * sc;

            // Escudo
            var shield = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(8 * sc, 1.8 * sc), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(13.5 * sc, 3.5 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(13.5 * sc, 9 * sc), true));
            fig.Segments.Add(new BezierSegment(new Point(13.5 * sc, 12.5 * sc), new Point(10 * sc, 14.5 * sc), new Point(8 * sc, 15 * sc), true));
            fig.Segments.Add(new BezierSegment(new Point(6 * sc, 14.5 * sc), new Point(2.5 * sc, 12.5 * sc), new Point(2.5 * sc, 9 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(2.5 * sc, 3.5 * sc), true));
            shield.Figures.Add(fig);
            dc.DrawGeometry(White, P(w), shield);

            // Checkmark ✔ grueso
            var check = new PathGeometry();
            var fCheck = new PathFigure { StartPoint = new Point(5.5 * sc, 8.2 * sc), IsClosed = false };
            fCheck.Segments.Add(new LineSegment(new Point(7.5 * sc, 10.5 * sc), true));
            fCheck.Segments.Add(new LineSegment(new Point(11 * sc, 5.8 * sc), true));
            check.Figures.Add(fCheck);
            dc.DrawGeometry(null, P(1.8 * sc), check);
        }

        // -------------------------------------------------------------------------
        // 8. Explorador Plantilla Riego (Gota de agua con arcos de aspersor)
        // -------------------------------------------------------------------------
        static void DrawRiegoPlantilla(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.3 * sc;

            // Gota de agua limpia
            var drop = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(8 * sc, 2 * sc), IsClosed = true };
            fig.Segments.Add(new BezierSegment(new Point(4 * sc, 6.5 * sc), new Point(3.5 * sc, 10.5 * sc), new Point(8 * sc, 14.2 * sc), true));
            fig.Segments.Add(new BezierSegment(new Point(12.5 * sc, 10.5 * sc), new Point(12 * sc, 6.5 * sc), new Point(8 * sc, 2 * sc), true));
            drop.Figures.Add(fig);
            dc.DrawGeometry(White, P(w), drop);

            // Rayos/Gotas concéntricas de aspersión
            dc.DrawLine(P(1.0 * sc), new Point(8 * sc, 6 * sc), new Point(8 * sc, 10.5 * sc));
            dc.DrawLine(P(1.0 * sc), new Point(6 * sc, 8.5 * sc), new Point(10 * sc, 8.5 * sc));
        }

        // -------------------------------------------------------------------------
        // 9. Plumbing Sheet (Tubería en T con válvula / bridas técnicas)
        // -------------------------------------------------------------------------
        static void DrawPlumbingSheet(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.3 * sc;

            // Tubo horizontal
            dc.DrawRectangle(White, P(w), new Rect(1.5 * sc, 6.5 * sc, 13 * sc, 3.5 * sc));
            // Bridas en los extremos
            dc.DrawRectangle(Ink, null, new Rect(1.5 * sc, 5.8 * sc, 1.2 * sc, 4.9 * sc));
            dc.DrawRectangle(Ink, null, new Rect(13.3 * sc, 5.8 * sc, 1.2 * sc, 4.9 * sc));

            // Derivación en T hacia abajo
            dc.DrawRectangle(White, P(w), new Rect(6.2 * sc, 9.8 * sc, 3.6 * sc, 4.5 * sc));
            dc.DrawRectangle(Ink, null, new Rect(5.5 * sc, 13.5 * sc, 5 * sc, 1.2 * sc));

            // Válvula / Llave de paso arriba
            dc.DrawRectangle(Ink, null, new Rect(7.2 * sc, 3.5 * sc, 1.6 * sc, 3 * sc));
            dc.DrawRoundedRectangle(Ink, null, new Rect(5 * sc, 2 * sc, 6 * sc, 1.8 * sc), 0.8 * sc, 0.8 * sc);
        }

        // -------------------------------------------------------------------------
        // 10. Generar Familias de Ladrillos (Ladrillo tolete con 3 huecos y +)
        // -------------------------------------------------------------------------
        static void DrawGenLadrillos(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.3 * sc;

            // Bloque ladrillo principal
            dc.DrawRoundedRectangle(White, P(w), new Rect(1.5 * sc, 4.5 * sc, 13 * sc, 8.5 * sc), 1.2 * sc, 1.2 * sc);

            // 3 perforaciones / huecos circulares
            dc.DrawEllipse(LightTint, P(0.9 * sc), new Point(4.5 * sc, 8.75 * sc), 1.4 * sc, 1.4 * sc);
            dc.DrawEllipse(LightTint, P(0.9 * sc), new Point(8 * sc, 8.75 * sc), 1.4 * sc, 1.4 * sc);
            dc.DrawEllipse(LightTint, P(0.9 * sc), new Point(11.5 * sc, 8.75 * sc), 1.4 * sc, 1.4 * sc);

            // Badge '+' en esquina superior derecha
            dc.DrawEllipse(Ink, null, new Point(12.5 * sc, 3.5 * sc), 2.5 * sc, 2.5 * sc);
            dc.DrawLine(WhitePen(1.1 * sc), new Point(12.5 * sc, 2 * sc), new Point(12.5 * sc, 5 * sc));
            dc.DrawLine(WhitePen(1.1 * sc), new Point(11 * sc, 3.5 * sc), new Point(14 * sc, 3.5 * sc));
        }

        // -------------------------------------------------------------------------
        // 11. Convertir Muros a Ladrillos (Muro trabado / aparejo de ladrillos)
        // -------------------------------------------------------------------------
        static void DrawConvMuros(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.3 * sc;

            // Marco del muro
            dc.DrawRectangle(White, P(w), new Rect(1.5 * sc, 2 * sc, 13 * sc, 12 * sc));

            // Hiladas horizontales
            dc.DrawLine(P(0.9 * sc), new Point(1.5 * sc, 6 * sc), new Point(14.5 * sc, 6 * sc));
            dc.DrawLine(P(0.9 * sc), new Point(1.5 * sc, 10 * sc), new Point(14.5 * sc, 10 * sc));

            // Juntas verticales trabadas (Running bond)
            // Hilada 1
            dc.DrawLine(P(0.9 * sc), new Point(8 * sc, 2 * sc), new Point(8 * sc, 6 * sc));
            // Hilada 2 (trabada)
            dc.DrawLine(P(0.9 * sc), new Point(4.8 * sc, 6 * sc), new Point(4.8 * sc, 10 * sc));
            dc.DrawLine(P(0.9 * sc), new Point(11.2 * sc, 6 * sc), new Point(11.2 * sc, 10 * sc));
            // Hilada 3
            dc.DrawLine(P(0.9 * sc), new Point(8 * sc, 10 * sc), new Point(8 * sc, 14 * sc));
        }

        // -------------------------------------------------------------------------
        // 12. Cómputos de Albañilería (Tabla de cuantificación + icono ladrillo)
        // -------------------------------------------------------------------------
        static void DrawTablasLadrillos(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.3 * sc;

            // Tabla
            dc.DrawRectangle(White, P(w), new Rect(1.5 * sc, 2 * sc, 13 * sc, 12 * sc));
            dc.DrawRectangle(LightTint, P(0.9 * sc), new Rect(1.5 * sc, 2 * sc, 13 * sc, 3.5 * sc));
            dc.DrawLine(P(0.9 * sc), new Point(1.5 * sc, 8.5 * sc), new Point(14.5 * sc, 8.5 * sc));
            dc.DrawLine(P(0.9 * sc), new Point(1.5 * sc, 11.5 * sc), new Point(14.5 * sc, 11.5 * sc));
            dc.DrawLine(P(0.9 * sc), new Point(7 * sc, 2 * sc), new Point(7 * sc, 14 * sc));

            // Mini silueta de ladrillo en encabezado
            dc.DrawRectangle(Ink, null, new Rect(3 * sc, 3 * sc, 3 * sc, 1.8 * sc));
        }

        // -------------------------------------------------------------------------
        // 13. Gestor de Piezas y Baldosas (Cuadrícula 2x2 de baldosas con +)
        // -------------------------------------------------------------------------
        static void DrawGenPisos(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.2 * sc;

            // 4 baldosas
            dc.DrawRectangle(White, P(w), new Rect(2 * sc, 2 * sc, 5.2 * sc, 5.2 * sc));
            dc.DrawRectangle(LightTint, P(w), new Rect(8.8 * sc, 2 * sc, 5.2 * sc, 5.2 * sc));
            dc.DrawRectangle(LightTint, P(w), new Rect(2 * sc, 8.8 * sc, 5.2 * sc, 5.2 * sc));
            dc.DrawRectangle(White, P(w), new Rect(8.8 * sc, 8.8 * sc, 5.2 * sc, 5.2 * sc));

            // Badge '+' en esquina superior
            dc.DrawEllipse(Ink, null, new Point(12.5 * sc, 3.5 * sc), 2.5 * sc, 2.5 * sc);
            dc.DrawLine(WhitePen(1.1 * sc), new Point(12.5 * sc, 2 * sc), new Point(12.5 * sc, 5 * sc));
            dc.DrawLine(WhitePen(1.1 * sc), new Point(11 * sc, 3.5 * sc), new Point(14 * sc, 3.5 * sc));
        }

        // -------------------------------------------------------------------------
        // 14. Instanciador de Enchapes & Pisos (Plano perspectiva con baldosa colocada)
        // -------------------------------------------------------------------------
        static void DrawConvPisos(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.3 * sc;

            // Piso en perspectiva isométrica
            var floor = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(4.5 * sc, 3 * sc), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(11.5 * sc, 3 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(15 * sc, 13.5 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(1 * sc, 13.5 * sc), true));
            floor.Figures.Add(fig);
            dc.DrawGeometry(White, P(w), floor);

            // Líneas de juntas en perspectiva
            dc.DrawLine(P(0.8 * sc), new Point(8 * sc, 3 * sc), new Point(8 * sc, 13.5 * sc));
            dc.DrawLine(P(0.8 * sc), new Point(3 * sc, 7.5 * sc), new Point(13 * sc, 7.5 * sc));
            dc.DrawLine(P(0.8 * sc), new Point(2 * sc, 10.5 * sc), new Point(14 * sc, 10.5 * sc));

            // Baldosa activa colocada
            dc.DrawRectangle(Ink, null, new Rect(5 * sc, 8 * sc, 6 * sc, 2.2 * sc));
        }

        // -------------------------------------------------------------------------
        // 15. Cómputos de Enchapes & Zócalos (Tabla con mini baldosa)
        // -------------------------------------------------------------------------
        static void DrawTablasPisos(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.3 * sc;

            // Tabla
            dc.DrawRectangle(White, P(w), new Rect(1.5 * sc, 2 * sc, 13 * sc, 12 * sc));
            dc.DrawRectangle(LightTint, P(0.9 * sc), new Rect(1.5 * sc, 2 * sc, 13 * sc, 3.5 * sc));
            dc.DrawLine(P(0.9 * sc), new Point(1.5 * sc, 8.5 * sc), new Point(14.5 * sc, 8.5 * sc));
            dc.DrawLine(P(0.9 * sc), new Point(1.5 * sc, 11.5 * sc), new Point(14.5 * sc, 11.5 * sc));
            dc.DrawLine(P(0.9 * sc), new Point(7 * sc, 2 * sc), new Point(7 * sc, 14 * sc));

            // Mini baldosa 2x2 en encabezado
            dc.DrawRectangle(Ink, null, new Rect(3 * sc, 3 * sc, 2 * sc, 2 * sc));
        }

        // -------------------------------------------------------------------------
        // 16. Gestor de Categorías (Sándwich de capas constructivas + etiqueta)
        // -------------------------------------------------------------------------
        static void DrawGestorCategorias(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.2 * sc;

            // Capa 1: Acabado baldosas (superior dividida)
            dc.DrawRectangle(White, P(w), new Rect(1.5 * sc, 3 * sc, 5.8 * sc, 2.8 * sc));
            dc.DrawRectangle(White, P(w), new Rect(7.8 * sc, 3 * sc, 5.8 * sc, 2.8 * sc));

            // Capa 2: Pega mortero
            dc.DrawRectangle(LightTint, P(w), new Rect(1.5 * sc, 6.5 * sc, 12.1 * sc, 2.2 * sc));

            // Capa 3: Losa de concreto (base gruesa)
            dc.DrawRectangle(White, P(w), new Rect(1.5 * sc, 9.4 * sc, 12.1 * sc, 4.2 * sc));

            // Tag / Etiqueta circular de categoría
            dc.DrawEllipse(Ink, null, new Point(12.5 * sc, 3.5 * sc), 2.5 * sc, 2.5 * sc);
            dc.DrawEllipse(White, null, new Point(12.5 * sc, 3.5 * sc), 1.0 * sc, 1.0 * sc);
        }

        // -------------------------------------------------------------------------
        // 17. Gestor de Perfiles Steel Deck (Onda corrugada trapezoidal con +)
        // -------------------------------------------------------------------------
        static void DrawGenSteel(DrawingContext dc, double s)
        {
            double sc = s / 16.0;

            // Perfil de lámina colaborante
            var deck = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(1 * sc, 11 * sc), IsClosed = false };
            fig.Segments.Add(new LineSegment(new Point(4 * sc, 11 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(6 * sc, 5 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(10 * sc, 5 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(12 * sc, 11 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(15 * sc, 11 * sc), true));
            deck.Figures.Add(fig);
            dc.DrawGeometry(null, P(2.2 * sc), deck);

            // Badge '+' en esquina superior
            dc.DrawEllipse(Ink, null, new Point(12.5 * sc, 3.5 * sc), 2.5 * sc, 2.5 * sc);
            dc.DrawLine(WhitePen(1.1 * sc), new Point(12.5 * sc, 2 * sc), new Point(12.5 * sc, 5 * sc));
            dc.DrawLine(WhitePen(1.1 * sc), new Point(11 * sc, 3.5 * sc), new Point(14 * sc, 3.5 * sc));
        }

        // -------------------------------------------------------------------------
        // 18. Modelador de Steel Decks (Losa de concreto sobre lámina corrugada)
        // -------------------------------------------------------------------------
        static void DrawConvSteel(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.3 * sc;

            // Losa de concreto superior
            dc.DrawRectangle(White, P(w), new Rect(1.5 * sc, 2.5 * sc, 13 * sc, 4.5 * sc));

            // Lámina corrugada inferior rellena
            var composite = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(1.5 * sc, 13.5 * sc), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(1.5 * sc, 7 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(14.5 * sc, 7 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(14.5 * sc, 13.5 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(12 * sc, 13.5 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(10 * sc, 9.5 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(6 * sc, 9.5 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(4 * sc, 13.5 * sc), true));
            composite.Figures.Add(fig);
            dc.DrawGeometry(Ink, null, composite);
        }

        // -------------------------------------------------------------------------
        // 19. Cómputos de Steel Decks (Tabla con mini perfil deck)
        // -------------------------------------------------------------------------
        static void DrawTablasSteel(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.3 * sc;

            // Tabla
            dc.DrawRectangle(White, P(w), new Rect(1.5 * sc, 2 * sc, 13 * sc, 12 * sc));
            dc.DrawRectangle(LightTint, P(0.9 * sc), new Rect(1.5 * sc, 2 * sc, 13 * sc, 3.5 * sc));
            dc.DrawLine(P(0.9 * sc), new Point(1.5 * sc, 8.5 * sc), new Point(14.5 * sc, 8.5 * sc));
            dc.DrawLine(P(0.9 * sc), new Point(1.5 * sc, 11.5 * sc), new Point(14.5 * sc, 11.5 * sc));
            dc.DrawLine(P(0.9 * sc), new Point(7 * sc, 2 * sc), new Point(7 * sc, 14 * sc));

            // Mini corrugado en encabezado
            dc.DrawLine(P(1.0 * sc), new Point(2.5 * sc, 4.2 * sc), new Point(3.5 * sc, 4.2 * sc));
            dc.DrawLine(P(1.0 * sc), new Point(3.5 * sc, 4.2 * sc), new Point(4.2 * sc, 2.8 * sc));
            dc.DrawLine(P(1.0 * sc), new Point(4.2 * sc, 2.8 * sc), new Point(5.2 * sc, 2.8 * sc));
            dc.DrawLine(P(1.0 * sc), new Point(5.2 * sc, 2.8 * sc), new Point(6.0 * sc, 4.2 * sc));
        }

        // -------------------------------------------------------------------------
        // 20. Plan Ambientador (Planta arquitectónica de habitación con puerta)
        // -------------------------------------------------------------------------
        static void DrawPlanAmbientador(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.5 * sc;

            // Muros perimetrales de la habitación
            dc.DrawLine(P(w), new Point(2 * sc, 2 * sc), new Point(14 * sc, 2 * sc));
            dc.DrawLine(P(w), new Point(14 * sc, 2 * sc), new Point(14 * sc, 14 * sc));
            dc.DrawLine(P(w), new Point(14 * sc, 14 * sc), new Point(8 * sc, 14 * sc));
            dc.DrawLine(P(w), new Point(2 * sc, 14 * sc), new Point(2 * sc, 2 * sc));

            // Abatimiento de puerta (arco de 90° con hoja de puerta)
            var doorArc = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(8 * sc, 14 * sc), IsClosed = false };
            fig.Segments.Add(new ArcSegment(new Point(3 * sc, 9 * sc), new Size(5 * sc, 5 * sc), 0, false, SweepDirection.Counterclockwise, true));
            doorArc.Figures.Add(fig);
            dc.DrawGeometry(null, P(0.8 * sc), doorArc);

            // Hoja de la puerta
            dc.DrawLine(P(1.1 * sc), new Point(8 * sc, 14 * sc), new Point(3 * sc, 9 * sc));
        }

        // -------------------------------------------------------------------------
        // 21. Gestor de Detalles Constructivos (Escuadra arquitectónica técnica)
        // -------------------------------------------------------------------------
        static void DrawDetalles(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.3 * sc;

            // Escuadra exterior
            var tri = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(2.5 * sc, 13.5 * sc), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(13.5 * sc, 13.5 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(2.5 * sc, 2.5 * sc), true));
            tri.Figures.Add(fig);
            dc.DrawGeometry(White, P(w), tri);

            // Calado triangular interno
            var cut = new PathGeometry();
            var fCut = new PathFigure { StartPoint = new Point(4.5 * sc, 11.8 * sc), IsClosed = true };
            fCut.Segments.Add(new LineSegment(new Point(10 * sc, 11.8 * sc), true));
            fCut.Segments.Add(new LineSegment(new Point(4.5 * sc, 6.3 * sc), true));
            cut.Figures.Add(fCut);
            dc.DrawGeometry(LightTint, P(0.9 * sc), cut);

            // Marcas de graduación / escala
            dc.DrawLine(P(0.8 * sc), new Point(2.5 * sc, 5 * sc), new Point(3.5 * sc, 5 * sc));
            dc.DrawLine(P(0.8 * sc), new Point(2.5 * sc, 8 * sc), new Point(3.8 * sc, 8 * sc));
            dc.DrawLine(P(0.8 * sc), new Point(2.5 * sc, 11 * sc), new Point(3.5 * sc, 11 * sc));
            dc.DrawLine(P(0.8 * sc), new Point(6 * sc, 13.5 * sc), new Point(6 * sc, 12.5 * sc));
            dc.DrawLine(P(0.8 * sc), new Point(9 * sc, 13.5 * sc), new Point(9 * sc, 12.2 * sc));
        }

        // -------------------------------------------------------------------------
        // 22. Manual & Mapa de la App (Libro abierto con cinta marcapáginas)
        // -------------------------------------------------------------------------
        static void DrawManual(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            double w = 1.3 * sc;

            // Libro abierto
            var book = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(8 * sc, 4 * sc), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(13.5 * sc, 2.5 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(13.5 * sc, 12.5 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(8 * sc, 14 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(2.5 * sc, 12.5 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(2.5 * sc, 2.5 * sc), true));
            book.Figures.Add(fig);
            dc.DrawGeometry(White, P(w), book);

            // Lomo central
            dc.DrawLine(P(1.2 * sc), new Point(8 * sc, 4 * sc), new Point(8 * sc, 14 * sc));

            // Líneas de texto página izquierda
            dc.DrawLine(P(0.8 * sc), new Point(4 * sc, 5.5 * sc), new Point(6.8 * sc, 5.5 * sc));
            dc.DrawLine(P(0.8 * sc), new Point(4 * sc, 7.5 * sc), new Point(6.8 * sc, 7.5 * sc));
            dc.DrawLine(P(0.8 * sc), new Point(4 * sc, 9.5 * sc), new Point(6.8 * sc, 9.5 * sc));

            // Líneas de texto página derecha
            dc.DrawLine(P(0.8 * sc), new Point(9.2 * sc, 5.5 * sc), new Point(12 * sc, 5.5 * sc));
            dc.DrawLine(P(0.8 * sc), new Point(9.2 * sc, 7.5 * sc), new Point(12 * sc, 7.5 * sc));
            dc.DrawLine(P(0.8 * sc), new Point(9.2 * sc, 9.5 * sc), new Point(12 * sc, 9.5 * sc));

            // Marcapáginas colgante arriba
            dc.DrawRectangle(Ink, null, new Rect(7.3 * sc, 1.5 * sc, 1.4 * sc, 3.5 * sc));
        }

        // Helper para destello ✦ IA consistente
        static void DrawSparkle(DrawingContext dc, double cx, double cy, double r)
        {
            var sparkle = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(cx, cy - r), IsClosed = true };
            fig.Segments.Add(new BezierSegment(new Point(cx, cy), new Point(cx, cy), new Point(cx + r, cy), true));
            fig.Segments.Add(new BezierSegment(new Point(cx, cy), new Point(cx, cy), new Point(cx, cy + r), true));
            fig.Segments.Add(new BezierSegment(new Point(cx, cy), new Point(cx, cy), new Point(cx - r, cy), true));
            fig.Segments.Add(new BezierSegment(new Point(cx, cy), new Point(cx, cy), new Point(cx, cy - r), true));
            sparkle.Figures.Add(fig);
            dc.DrawGeometry(Ink, null, sparkle);
        }
    }
}
