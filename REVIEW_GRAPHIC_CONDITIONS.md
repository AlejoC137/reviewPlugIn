# 📐 ESTÁNDAR OBLIGATORIO DE CONDICIONES GRÁFICAS — ARCA REVIEW

> [!IMPORTANT]
> **REGLA DE ORO DEL PROYECTO REVIEW:**  
> **Todas las ventanas (Windows), páginas acoplables (Dockable Panes), cuadros de diálogo, asistentes y controles interactivos creados o modificados en este plugin DEBEN basarse estricta y obligatoriamente en este sistema de diseño.**
> Está terminantemente prohibido inventar paletas oscuras aleatorias, esquemas de color ad-hoc (azules eléctricos, morados, bordes redondeados tipo móvil) o estilos divergentes. La identidad gráfica de Review es **técnica, arquitectónica, minimalista (Warm Architectural Linen & Navy Blueprint Sharp Card)**.

---

## 🎨 1. Paleta de Colores Oficial (Design Tokens)

| Token / Recurso | Hex Color | Muestra | Uso Obligatorio |
| :--- | :--- | :--- | :--- |
| `ArcaBg` | `#FCF9F4` | 🟨 Linen Cálido | Fondo principal de ventanas, pestañas y páginas. |
| `ArcaSurface` | `#F6F3EE` | ◽ Superficie Suave | Fondo de tarjetas contenedor (`CardBorder`), encabezados y barras. |
| `ArcaSurfaceHighlight` | `#E5E2DD` | ◽ Hover Superficie | Estados hover en botones secundarios y elementos de lista. |
| `ArcaCardBg` | `#FFFFFF` | ⬜ Blanco Puro | Fondo de cajas de texto, tarjetas métricas y listados. |
| `ArcaPrimary` | `#0F4369` | 🟦 Azul Blueprint | Títulos principales, botones primarios, acentos e identidad. |
| `ArcaPrimaryHover` | `#1C1C19` | ⬛ Tinta Oscura | Hover de botones primarios. |
| `ArcaPrimaryContainer` | `#D1E4F9` | 🔷 Ice Blueprint | Selección activa de items, badges de versión y checks marcados. |
| `ArcaInk` | `#1C1C19` | ⬛ Tinta Negra Carbón | Texto principal de lectura y bordes obligatorios de alto contraste. |
| `ArcaMuted` | `#72777F` | ▫️ Gris Técnico | Textos secundarios, descripciones, subtítulos e hints. |
| `ArcaBorder` | `#1C1C19` | ⬛ Borde Tinta (1px) | Bordes estructurales de tarjetas, botones y controles. |
| `ArcaOutlineVariant` | `#C2C7CF` | ▫️ Separador Neutro | Separadores sutiles y divisores secundarios. |
| `ArcaSuccess` | `#059669` | 🟩 Esmeralda Técnico | Badges de éxito, acciones de confirmación ejecutiva. |
| `ArcaSuccessBg` | `#D1FAE5` | 🟩 Contenedor Éxito | Fondos de estado completado / en línea. |
| `ArcaSuccessText` | `#065F46` | 🟩 Texto Éxito | Texto sobre fondos verdes. |
| `ArcaError` | `#BA1A1A` | 🟥 Rojo Alerta | Notificaciones de error y advertencias críticas. |
| `ArcaErrorBg` | `#FFDAD6` | 🟥 Contenedor Error | Fondos de alerta / desconexión. |

---

## 🏛️ 2. Tipografía y Estructura Visual

- **Fuente Principal:** `Segoe UI, Inter, sans-serif`
- **Fuente Técnica / Código:** `Consolas, monospace` (para IDs de Supabase, JSONs, terminales y logs).
- **Esquinas y Geometría:** `CornerRadius="0"` (estilo blueprint técnico con bordes rectos) o máximo `CornerRadius="2"` para tabs/pills. **Sin bordes hiper-redondeados.**
- **Bordes:** Grosor uniforme de `1` o `1.5` con brocha `#1C1C19` o `#C2C7CF`.
- **Línea Superior de Identidad:** Toda ventana debe comenzar en `Grid.Row="0"` con un `<Rectangle Height="3" Fill="#0F4369"/>`.

---

## 🎛️ 3. Componentes Estándar Disponibles (`ReviewDesignSystem.xaml`)

1. **Botones Primarios (`PrimaryButton`):**
   - Fondo: `#0F4369`, Texto: `#FFFFFF`, Borde: `#1C1C19`, Hover: `#1C1C19`.
   - Para la acción principal del contexto (Guardar, Escanear, Exportar).
2. **Botones Secundarios (`SecondaryButton`):**
   - Fondo: `#F6F3EE`, Texto: `#1C1C19`, Borde: `#1C1C19`, Hover: `#E5E2DD` + Texto `#0F4369`.
   - Para acciones auxiliares, navegación o filtros.
3. **Botones de Éxito (`SuccessButton`):**
   - Fondo: `#059669` o `#D1FAE5`, Texto: `#FFFFFF` o `#065F46`.
   - Para disparadores de ejecución pesada o aplicación de cambios.
4. **Tarjetas Contenedor (`CardBorder`):**
   - Fondo: `#F6F3EE`, Borde: `#1C1C19`, Padding: `10`.
5. **Cajas de Entrada (`ArcaTextBox`):**
   - Fondo: `#FFFFFF`, Borde: `#1C1C19`, Texto: `#1C1C19`.
6. **Casillas de Verificación (`ArcaCheckBox`):**
   - Checkbox cuadrado técnico con tilde Blueprint `#0F4369` y relleno `#D1E4F9` al marcar.
7. **Consola / Terminal de Diagnóstico (`ArcaTerminalBorder`):**
   - Fondo: `#1C1C19`, Borde: `#0F4369`, Texto: `#E5E2DD`, Fuente: `Consolas, 9.5pt`.

---

## 🔐 4. Directiva de Autenticación Unificada (Single Sign-On)

- **CERO inicios de sesión duplicados:** Todas las ventanas deben leer `AuthenticationService.UserEmail` y `AuthenticationService.IsLoggedIn`.
- **Ninguna ventana secundaria o herramienta debe tener campos de `PasswordBox` o pedir usuario/clave repetidamente.**
- El Panel de Control (`ReviewControlPanelPage`) es el único gestor central de sesión.

---

## 📤 5. Directiva de Exportación de Metadata

- **La exportación de metadatos del proyecto (`ifcJSON`, `.json` de metadata completa y modelo 3D Web) es para archivos locales (para IA, visualizadores R3F, análisis y backups).**
- **NO debe estar vinculada ni forzada a la sincronización de base de datos.**
- La sincronización a Supabase es un proceso independiente ubicado en el Centro de Sincronización.
