# 🤖 Antigravity & Claude MCP para Autodesk Revit (`reviewPlugIn`)

Bienvenido al núcleo de integración **Model Context Protocol (MCP)** desarrollado dentro de **`reviewPlugIn`**. 

Este sistema permite a agentes de Inteligencia Artificial locales como **Google Antigravity** y **Claude Desktop** interactuar, consultar y modificar modelos BIM abiertos en tiempo real dentro de Autodesk Revit, **sin depender de herramientas de terceros (como Nonica)** y **sin requerir servidores web en la nube ni puertos públicos**.

---

## 🏗️ Arquitectura de la Solución

```
+-----------------------------------------------------------+
|               AGENTE IA (Local Desktop)                  |
|          Google Antigravity  /  Claude Desktop            |
+-----------------------------------------------------------+
                             │
                     stdio (JSON-RPC)
                             ▼
+-----------------------------------------------------------+
|          Puente Local (Antigravity_MCP/bridge)            |
|                 revit-mcp-bridge.mjs                      |
+-----------------------------------------------------------+
                             │
                 HTTP Loopback (127.0.0.1:8080)
                             ▼
+-----------------------------------------------------------+
|              Autodesk Revit (.NET Process)                |
|  ┌─────────────────────────────────────────────────────┐  |
|  │ RevitMcpServer.cs (Listener Interno en C#)         │  |
|  │ McpExternalEventHandler.cs (Revit API Safe Thread)   │  |
|  │ DoorAssemblyManagerService.cs (Ensamblajes & Vistas) │  |
|  └─────────────────────────────────────────────────────┘  |
+-----------------------------------------------------------+
```

1. **Protocolo STDIO nativo**: Antigravity y Claude ejecutan `node revit-mcp-bridge.mjs` como un subproceso local.
2. **Despacho seguro en el hilo de Revit**: Toda modificación del modelo BIM se encola a través de `ExternalEvent` (`McpExternalEventHandler.cs`) para cumplir con las directivas de la API de Revit y evitar bloqueos.

---

## 🚀 Instalación Rápida (1 Clic)

1. Abre la carpeta `Antigravity_MCP/deploy/`.
2. Haz doble clic en **`install_mcp.cmd`** (o ejecuta `install_mcp.ps1` en PowerShell).
3. El script configurará automáticamente:
   * **Antigravity**: `%USERPROFILE%\.gemini\config\mcp_config.json`
   * **Skill BIM**: `%USERPROFILE%\.gemini\config\skills\revit-bim-agent\SKILL.md`
   * **Claude Desktop**: `%APPDATA%\Claude\claude_desktop_config.json`

---

## 🛠️ Catálogo de Herramientas Disponibles

| Herramienta | Descripción |
| :--- | :--- |
| `revit_get_active_session` | Consulta en vivo el nombre del proyecto, vista activa, ruta y usuario conectado. |
| `revit_get_selection` | Obtiene los elementos seleccionados con el ratón en la vista activa. |
| `revit_select_elements` | Selecciona elementos en pantalla por sus IDs y hace zoom sobre ellos. |
| `revit_query_elements` | Filtra elementos por categoría (*Doors*, *Walls*, *Floors*...), nivel o texto. |
| **`revit_create_door_assemblies`** | **Crea ensamblajes de puertas/ventanas (por tipo o ejemplar) y genera vistas técnicas de taller.** |
| `revit_get_element_parameters`| Inspecciona exhaustivamente parámetros de instancia y tipo de un elemento. |
| `revit_modify_parameters` | Cambia valores de parámetros en lote para uno o múltiples elementos. |
| `revit_manage_sheets` | Lista, crea láminas con TitleBlock clonado y renumera rangos en lote. |
| `revit_manage_views_and_viewports` | Duplica vistas y las coloca automáticamente en planos. |
| `revit_convertir_pisos_3d` | Modela baldosas físicas paramétricas sobre pisos de Revit. |
| `revit_crear_tablas_computos`| Genera tablas de cuantificación y cómputos métricos. |
| `revit_sync_to_supabase` | Exporta la metadata completa del modelo a la base de datos Supabase. |

---

## 💬 Ejemplos de Prompts para Antigravity

Una vez abierto Revit y el modelo:

* *"Dime qué elementos tengo seleccionados en la pantalla de Revit."*
* *"Crea un ensamblaje por cada tipo de puerta con vistas de taller (planta, elevación y tabla)."*
* *"Busca todos los muros del nivel 1 y dime cuántos hay."*
* *"Cambia el comentario de los elementos seleccionados a 'Revisado por IA'."*
* *"Crea una nueva lámina A-105 clonando el cajetín de la lámina A-101."*

---

## 📂 Estructura de esta Carpeta

* **`bridge/`**: Contiene `revit-mcp-bridge.mjs` que conecta el estándar MCP por stdio con Revit.
* **`skills/`**: Contiene la skill `revit-bim-agent` con las directrices cognitivas para el modelo de IA.
* **`deploy/`**: Scripts automáticos de instalación y prueba de conexión rápida.
