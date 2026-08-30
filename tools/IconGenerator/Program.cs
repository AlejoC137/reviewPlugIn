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
            string outputDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            Console.WriteLine($"Generando iconos en: {outputDir}");

            var icons = new (string name, Action<DrawingContext, double> draw)[]
            {
                ("icon_gen_ia", DrawGenIA),
                ("icon_export_metadata", DrawExportMetadata),
                ("icon_config_plantilla", DrawConfigPlantilla),
                ("icon_tablas_ia", DrawTablasIA),
                ("icon_sync_materials", DrawSyncMaterials),
                ("icon_riego_plantilla", DrawRiegoPlantilla),
                ("icon_plumbing_sheet", DrawPlumbingSheet),
                ("icon_gen_ladrillos", DrawGenLadrillos),
                ("icon_conv_muros", DrawConvMuros),
                ("icon_tablas_ladrillos", DrawTablasLadrillos),
                ("icon_gen_pisos", DrawGenPisos),
                ("icon_conv_pisos", DrawConvPisos),
                ("icon_tablas_pisos", DrawTablasPisos),
                ("icon_gen_steel", DrawGenSteel),
                ("icon_conv_steel", DrawConvSteel),
                ("icon_tablas_steel", DrawTablasSteel),
                ("icon_manual", DrawManual)
            };

            foreach (var icon in icons)
            {
                // 16x16 para Image (Ribbon stacked items)
                RenderAndSave(icon.draw, 16, Path.Combine(outputDir, $"{icon.name}.png"));
                // 32x32 para LargeImage
                RenderAndSave(icon.draw, 32, Path.Combine(outputDir, $"{icon.name}_32.png"));
                Console.WriteLine($"Icono generado: {icon.name}");
            }

            Console.WriteLine("¡Todos los iconos fueron generados exitosamente!");
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

        // Helpers de dibujo
        static SolidColorBrush B(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));
        static Pen P(SolidColorBrush brush, double thickness) => new Pen(brush, thickness);

        // 1. Generador Familias IA: Cubo 3D morado/azul con destello dorado
        static void DrawGenIA(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            // Cubo base
            var brushBase = B(79, 70, 229); // Indigo
            var brushTop = B(129, 140, 248);
            var brushRight = B(67, 56, 202);

            // Cubo isometrico
            var pathTop = new PathGeometry();
            var figTop = new PathFigure { StartPoint = new Point(8 * sc, 1.5 * sc), IsClosed = true };
            figTop.Segments.Add(new LineSegment(new Point(14 * sc, 4.5 * sc), true));
            figTop.Segments.Add(new LineSegment(new Point(8 * sc, 7.5 * sc), true));
            figTop.Segments.Add(new LineSegment(new Point(2 * sc, 4.5 * sc), true));
            pathTop.Figures.Add(figTop);
            dc.DrawGeometry(brushTop, null, pathTop);

            var pathLeft = new PathGeometry();
            var figLeft = new PathFigure { StartPoint = new Point(2 * sc, 4.5 * sc), IsClosed = true };
            figLeft.Segments.Add(new LineSegment(new Point(8 * sc, 7.5 * sc), true));
            figLeft.Segments.Add(new LineSegment(new Point(8 * sc, 14.5 * sc), true));
            figLeft.Segments.Add(new LineSegment(new Point(2 * sc, 11.5 * sc), true));
            pathLeft.Figures.Add(figLeft);
            dc.DrawGeometry(brushBase, null, pathLeft);

            var pathRight = new PathGeometry();
            var figRight = new PathFigure { StartPoint = new Point(8 * sc, 7.5 * sc), IsClosed = true };
            figRight.Segments.Add(new LineSegment(new Point(14 * sc, 4.5 * sc), true));
            figRight.Segments.Add(new LineSegment(new Point(14 * sc, 11.5 * sc), true));
            figRight.Segments.Add(new LineSegment(new Point(8 * sc, 14.5 * sc), true));
            pathRight.Figures.Add(figRight);
            dc.DrawGeometry(brushRight, null, pathRight);

            // Destello IA (estrella dorada)
            var sparkBrush = B(251, 191, 36); // Amber
            dc.DrawEllipse(sparkBrush, null, new Point(12 * sc, 3.5 * sc), 1.8 * sc, 1.8 * sc);
        }

        // 2. Exportar Metadata: Base de datos cilíndrica con flecha de salida verde
        static void DrawExportMetadata(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            var dbBrush = B(14, 165, 233); // Cyan/Sky
            var dbDark = B(2, 132, 199);
            var greenBrush = B(34, 197, 94); // Green

            // Discos de base de datos
            dc.DrawRoundedRectangle(dbBrush, null, new Rect(2 * sc, 2 * sc, 8 * sc, 4 * sc), 2 * sc, 2 * sc);
            dc.DrawRoundedRectangle(dbDark, null, new Rect(2 * sc, 6.5 * sc, 8 * sc, 4 * sc), 2 * sc, 2 * sc);
            dc.DrawRoundedRectangle(dbBrush, null, new Rect(2 * sc, 11 * sc, 8 * sc, 4 * sc), 2 * sc, 2 * sc);

            // Flecha verde de exportar
            var arrow = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(9 * sc, 8 * sc), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(12 * sc, 8 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(12 * sc, 6 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(15.5 * sc, 9.5 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(12 * sc, 13 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(12 * sc, 11 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(9 * sc, 11 * sc), true));
            arrow.Figures.Add(fig);
            dc.DrawGeometry(greenBrush, P(B(255, 255, 255), 0.5 * sc), arrow);
        }

        // 3. Configurador Plantillas: Engranaje + deslizadores / archivo
        static void DrawConfigPlantilla(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            var gearBrush = B(100, 116, 139); // Slate
            var accent = B(59, 130, 246); // Blue

            // Hoja plantilla
            dc.DrawRoundedRectangle(B(241, 245, 249), P(gearBrush, 1 * sc), new Rect(2 * sc, 1.5 * sc, 10 * sc, 13 * sc), 1 * sc, 1 * sc);
            // Líneas de plantilla
            dc.DrawLine(P(accent, 1.5 * sc), new Point(4 * sc, 4.5 * sc), new Point(9 * sc, 4.5 * sc));
            dc.DrawLine(P(gearBrush, 1 * sc), new Point(4 * sc, 7.5 * sc), new Point(8 * sc, 7.5 * sc));
            dc.DrawLine(P(gearBrush, 1 * sc), new Point(4 * sc, 10.5 * sc), new Point(7 * sc, 10.5 * sc));

            // Engranaje / Tuerca de config
            dc.DrawEllipse(accent, null, new Point(12 * sc, 11 * sc), 3.5 * sc, 3.5 * sc);
            dc.DrawEllipse(B(255, 255, 255), null, new Point(12 * sc, 11 * sc), 1.5 * sc, 1.5 * sc);
        }

        // 4. Asistente Tablas IA: Tabla verde con destello IA
        static void DrawTablasIA(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            var tblHeader = B(16, 185, 129); // Emerald
            var tblBody = B(209, 250, 229);
            var tblBorder = B(5, 150, 105);

            // Tabla
            dc.DrawRectangle(tblBody, P(tblBorder, 1 * sc), new Rect(1.5 * sc, 2 * sc, 13 * sc, 12 * sc));
            // Encabezado
            dc.DrawRectangle(tblHeader, null, new Rect(1.5 * sc, 2 * sc, 13 * sc, 3.5 * sc));

            // Cuadrícula
            dc.DrawLine(P(tblBorder, 0.8 * sc), new Point(1.5 * sc, 8.5 * sc), new Point(14.5 * sc, 8.5 * sc));
            dc.DrawLine(P(tblBorder, 0.8 * sc), new Point(1.5 * sc, 11.5 * sc), new Point(14.5 * sc, 11.5 * sc));
            dc.DrawLine(P(tblBorder, 0.8 * sc), new Point(6 * sc, 5.5 * sc), new Point(6 * sc, 14 * sc));
            dc.DrawLine(P(tblBorder, 0.8 * sc), new Point(10.5 * sc, 5.5 * sc), new Point(10.5 * sc, 14 * sc));

            // Sparkle IA
            dc.DrawEllipse(B(245, 158, 11), null, new Point(12.5 * sc, 3.5 * sc), 1.5 * sc, 1.5 * sc);
        }

        // 5. Sincronizar Materiales: Flechas circulares de sync con muestras de color
        static void DrawSyncMaterials(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            // Bolas de materiales
            dc.DrawEllipse(B(239, 68, 68), null, new Point(5 * sc, 8 * sc), 2.2 * sc, 2.2 * sc); // Red
            dc.DrawEllipse(B(59, 130, 246), null, new Point(11 * sc, 8 * sc), 2.2 * sc, 2.2 * sc); // Blue
            dc.DrawEllipse(B(16, 185, 129), null, new Point(8 * sc, 4.5 * sc), 2 * sc, 2 * sc); // Green

            // Flechas de sincronización
            var syncPen = P(B(30, 41, 59), 1.2 * sc);
            var arcPath = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(3.5 * sc, 11 * sc), IsClosed = false };
            fig.Segments.Add(new ArcSegment(new Point(12.5 * sc, 11 * sc), new Size(5 * sc, 4 * sc), 0, false, SweepDirection.Counterclockwise, true));
            arcPath.Figures.Add(fig);
            dc.DrawGeometry(null, syncPen, arcPath);

            // Punta de flecha
            dc.DrawLine(syncPen, new Point(12.5 * sc, 11 * sc), new Point(10.5 * sc, 9.5 * sc));
            dc.DrawLine(syncPen, new Point(12.5 * sc, 11 * sc), new Point(12.5 * sc, 8.5 * sc));
        }

        // 6. Explorador Plantilla Riego: Gota de agua + aspersor
        static void DrawRiegoPlantilla(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            var waterBlue = B(6, 182, 212); // Cyan
            var darkBlue = B(2, 132, 199);

            // Gota de agua
            var drop = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(8 * sc, 1.5 * sc), IsClosed = true };
            fig.Segments.Add(new BezierSegment(new Point(4 * sc, 6 * sc), new Point(3 * sc, 10 * sc), new Point(8 * sc, 14 * sc), true));
            fig.Segments.Add(new BezierSegment(new Point(13 * sc, 10 * sc), new Point(12 * sc, 6 * sc), new Point(8 * sc, 1.5 * sc), true));
            drop.Figures.Add(fig);
            dc.DrawGeometry(waterBlue, P(darkBlue, 0.8 * sc), drop);

            // Ondas de aspersión
            dc.DrawEllipse(null, P(B(255, 255, 255), 0.8 * sc), new Point(8 * sc, 9 * sc), 2.5 * sc, 2.5 * sc);
        }

        // 7. Plumbing Sheet: Tubería T en codo azul + plano
        static void DrawPlumbingSheet(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            var pipeBrush = B(2, 132, 199); // Blue
            var valveGold = B(245, 158, 11);

            // Tubería horizontal
            dc.DrawRoundedRectangle(pipeBrush, null, new Rect(1.5 * sc, 6 * sc, 13 * sc, 4 * sc), 1 * sc, 1 * sc);
            // Tubería vertical (Tee)
            dc.DrawRoundedRectangle(pipeBrush, null, new Rect(6 * sc, 1.5 * sc, 4 * sc, 6 * sc), 1 * sc, 1 * sc);

            // Válvula / Llave de paso
            dc.DrawRectangle(valveGold, null, new Rect(5 * sc, 1 * sc, 6 * sc, 2 * sc));
            dc.DrawEllipse(B(255, 255, 255), null, new Point(8 * sc, 8 * sc), 1.2 * sc, 1.2 * sc);
        }

        // 8. Generar Familias de Ladrillos: Ladrillo terracota con símbolo '+'
        static void DrawGenLadrillos(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            var brickBrush = B(194, 65, 12); // Terracotta / Rust
            var darkBorder = B(154, 52, 18);
            var white = B(255, 255, 255);

            // Ladrillo principal
            dc.DrawRoundedRectangle(brickBrush, P(darkBorder, 1 * sc), new Rect(1.5 * sc, 3.5 * sc, 13 * sc, 9 * sc), 1.5 * sc, 1.5 * sc);

            // Agujeros de ladrillo (huecos)
            dc.DrawEllipse(B(124, 45, 18), null, new Point(4.5 * sc, 8 * sc), 1.4 * sc, 1.4 * sc);
            dc.DrawEllipse(B(124, 45, 18), null, new Point(8 * sc, 8 * sc), 1.4 * sc, 1.4 * sc);
            dc.DrawEllipse(B(124, 45, 18), null, new Point(11.5 * sc, 8 * sc), 1.4 * sc, 1.4 * sc);

            // Badge '+' verde en esquina
            dc.DrawEllipse(B(34, 197, 94), null, new Point(12.5 * sc, 4 * sc), 3 * sc, 3 * sc);
            dc.DrawLine(P(white, 1 * sc), new Point(12.5 * sc, 2 * sc), new Point(12.5 * sc, 6 * sc));
            dc.DrawLine(P(white, 1 * sc), new Point(10.5 * sc, 4 * sc), new Point(14.5 * sc, 4 * sc));
        }

        // 9. Convertir Muros a Ladrillos: Muro continuo dividiéndose en ladrillos
        static void DrawConvMuros(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            var brick1 = B(234, 88, 12);
            var brick2 = B(194, 65, 12);
            var mortar = B(226, 232, 240); // Junta de pega clara

            // Fondo de mortero
            dc.DrawRectangle(mortar, null, new Rect(1.5 * sc, 2.5 * sc, 13 * sc, 11 * sc));

            // Hilada 1
            dc.DrawRectangle(brick1, null, new Rect(2 * sc, 3 * sc, 5.5 * sc, 3 * sc));
            dc.DrawRectangle(brick2, null, new Rect(8 * sc, 3 * sc, 6 * sc, 3 * sc));

            // Hilada 2 (trabada)
            dc.DrawRectangle(brick2, null, new Rect(2 * sc, 6.5 * sc, 3 * sc, 3 * sc));
            dc.DrawRectangle(brick1, null, new Rect(5.5 * sc, 6.5 * sc, 5.5 * sc, 3 * sc));
            dc.DrawRectangle(brick2, null, new Rect(11.5 * sc, 6.5 * sc, 2.5 * sc, 3 * sc));

            // Hilada 3
            dc.DrawRectangle(brick1, null, new Rect(2 * sc, 10 * sc, 5.5 * sc, 3 * sc));
            dc.DrawRectangle(brick2, null, new Rect(8 * sc, 10 * sc, 6 * sc, 3 * sc));
        }

        // 10. Crear Tablas Ladrillos: Tabla terracota de cómputos
        static void DrawTablasLadrillos(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            var header = B(194, 65, 12); // Terracotta
            var bg = B(255, 237, 213);
            var border = B(154, 52, 18);

            dc.DrawRectangle(bg, P(border, 0.8 * sc), new Rect(1.5 * sc, 2 * sc, 13 * sc, 12 * sc));
            dc.DrawRectangle(header, null, new Rect(1.5 * sc, 2 * sc, 13 * sc, 3.5 * sc));

            dc.DrawLine(P(border, 0.8 * sc), new Point(1.5 * sc, 8.5 * sc), new Point(14.5 * sc, 8.5 * sc));
            dc.DrawLine(P(border, 0.8 * sc), new Point(1.5 * sc, 11.5 * sc), new Point(14.5 * sc, 11.5 * sc));
            dc.DrawLine(P(border, 0.8 * sc), new Point(7 * sc, 5.5 * sc), new Point(7 * sc, 14 * sc));

            // Mini ladrillo en header
            dc.DrawRectangle(B(255, 255, 255), null, new Rect(3 * sc, 3 * sc, 4 * sc, 1.8 * sc));
        }

        // 11. Gestor de Piezas y Baldosas: Patrón de baldosas 2x2 en rombo / cuadrícula
        static void DrawGenPisos(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            var tile1 = B(245, 158, 11); // Amber
            var tile2 = B(217, 119, 6);
            var border = B(180, 83, 9);

            // 4 baldosas
            dc.DrawRectangle(tile1, P(border, 0.6 * sc), new Rect(2 * sc, 2 * sc, 5.5 * sc, 5.5 * sc));
            dc.DrawRectangle(tile2, P(border, 0.6 * sc), new Rect(8.5 * sc, 2 * sc, 5.5 * sc, 5.5 * sc));
            dc.DrawRectangle(tile2, P(border, 0.6 * sc), new Rect(2 * sc, 8.5 * sc, 5.5 * sc, 5.5 * sc));
            dc.DrawRectangle(tile1, P(border, 0.6 * sc), new Rect(8.5 * sc, 8.5 * sc, 5.5 * sc, 5.5 * sc));

            // Brillo diagonal
            dc.DrawLine(P(B(255, 255, 255), 1 * sc), new Point(3 * sc, 3 * sc), new Point(6 * sc, 6 * sc));
        }

        // 12. Instanciador de Enchapes & Pisos: Plano de piso con llana / colocación
        static void DrawConvPisos(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            var floorBg = B(245, 245, 244);
            var tileColor = B(217, 119, 6);
            var linePen = P(B(168, 162, 158), 0.8 * sc);

            // Piso en perspectiva
            var floor = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(4 * sc, 2 * sc), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(12 * sc, 2 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(15 * sc, 14 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(1 * sc, 14 * sc), true));
            floor.Figures.Add(fig);
            dc.DrawGeometry(floorBg, linePen, floor);

            // Baldosa colocándose
            dc.DrawRectangle(tileColor, P(B(180, 83, 9), 0.8 * sc), new Rect(4 * sc, 6 * sc, 8 * sc, 5 * sc));
        }

        // 13. Cómputos de Enchapes & Zócalos: Tabla ámbar/madera
        static void DrawTablasPisos(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            var header = B(217, 119, 6); // Amber
            var bg = B(254, 243, 199);
            var border = B(180, 83, 9);

            dc.DrawRectangle(bg, P(border, 0.8 * sc), new Rect(1.5 * sc, 2 * sc, 13 * sc, 12 * sc));
            dc.DrawRectangle(header, null, new Rect(1.5 * sc, 2 * sc, 13 * sc, 3.5 * sc));

            dc.DrawLine(P(border, 0.8 * sc), new Point(1.5 * sc, 8.5 * sc), new Point(14.5 * sc, 8.5 * sc));
            dc.DrawLine(P(border, 0.8 * sc), new Point(1.5 * sc, 11.5 * sc), new Point(14.5 * sc, 11.5 * sc));
            dc.DrawLine(P(border, 0.8 * sc), new Point(7 * sc, 5.5 * sc), new Point(7 * sc, 14 * sc));

            // Iconito baldosa
            dc.DrawRectangle(B(255, 255, 255), null, new Rect(3 * sc, 3 * sc, 3 * sc, 3 * sc));
        }

        // 14. Gestor de Perfiles Steel Deck: Perfil corrugado de acero (onda trapezoidal)
        static void DrawGenSteel(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            var steelBrush = B(71, 85, 105); // Steel Slate
            var lightSteel = B(148, 163, 184);

            var path = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(1 * sc, 11 * sc), IsClosed = false };
            fig.Segments.Add(new LineSegment(new Point(4 * sc, 11 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(6 * sc, 5 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(10 * sc, 5 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(12 * sc, 11 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(15 * sc, 11 * sc), true));
            path.Figures.Add(fig);

            dc.DrawGeometry(null, P(steelBrush, 2.5 * sc), path);
            dc.DrawGeometry(null, P(lightSteel, 1.2 * sc), path);

            // Badge '+'
            dc.DrawEllipse(B(59, 130, 246), null, new Point(12.5 * sc, 4 * sc), 2.5 * sc, 2.5 * sc);
        }

        // 15. Modelador de Steel Decks: Losa de concreto sobre deck corrugado
        static void DrawConvSteel(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            var concrete = B(203, 213, 225);
            var steel = B(51, 65, 85);

            // Concreto superior
            dc.DrawRectangle(concrete, P(steel, 0.8 * sc), new Rect(1.5 * sc, 2.5 * sc, 13 * sc, 4.5 * sc));

            // Placa colaborante inferior
            var path = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(1.5 * sc, 13 * sc), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(1.5 * sc, 7 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(14.5 * sc, 7 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(14.5 * sc, 13 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(12 * sc, 13 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(10 * sc, 9 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(6 * sc, 9 * sc), true));
            fig.Segments.Add(new LineSegment(new Point(4 * sc, 13 * sc), true));
            path.Figures.Add(fig);
            dc.DrawGeometry(steel, null, path);
        }

        // 16. Cómputos de Steel Decks: Tabla de cómputo gris/acero
        static void DrawTablasSteel(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            var header = B(71, 85, 105);
            var bg = B(241, 245, 249);
            var border = B(51, 65, 85);

            dc.DrawRectangle(bg, P(border, 0.8 * sc), new Rect(1.5 * sc, 2 * sc, 13 * sc, 12 * sc));
            dc.DrawRectangle(header, null, new Rect(1.5 * sc, 2 * sc, 13 * sc, 3.5 * sc));

            dc.DrawLine(P(border, 0.8 * sc), new Point(1.5 * sc, 8.5 * sc), new Point(14.5 * sc, 8.5 * sc));
            dc.DrawLine(P(border, 0.8 * sc), new Point(1.5 * sc, 11.5 * sc), new Point(14.5 * sc, 11.5 * sc));
            dc.DrawLine(P(border, 0.8 * sc), new Point(7 * sc, 5.5 * sc), new Point(7 * sc, 14 * sc));

            // Iconito corrugado
            dc.DrawLine(P(B(255, 255, 255), 1.2 * sc), new Point(3 * sc, 4 * sc), new Point(4.5 * sc, 2.5 * sc));
            dc.DrawLine(P(B(255, 255, 255), 1.2 * sc), new Point(4.5 * sc, 2.5 * sc), new Point(6 * sc, 4 * sc));
        }

        // 17. Manual & Mapa de la App: Libro / Documento azul con "i"
        static void DrawManual(DrawingContext dc, double s)
        {
            double sc = s / 16.0;
            var bookCover = B(30, 58, 138); // Dark Blue
            var pages = B(248, 250, 252);
            var bookmark = B(239, 68, 68); // Red

            // Libro abierto
            dc.DrawRoundedRectangle(bookCover, null, new Rect(1.5 * sc, 2.5 * sc, 13 * sc, 11 * sc), 1.5 * sc, 1.5 * sc);
            dc.DrawRectangle(pages, null, new Rect(3 * sc, 4 * sc, 10 * sc, 8 * sc));

            // Línea central (lomo)
            dc.DrawLine(P(B(148, 163, 184), 0.8 * sc), new Point(8 * sc, 4 * sc), new Point(8 * sc, 12 * sc));

            // Cinta marcapáginas
            dc.DrawRectangle(bookmark, null, new Rect(7.2 * sc, 1.5 * sc, 1.6 * sc, 5 * sc));
        }
    }
}
