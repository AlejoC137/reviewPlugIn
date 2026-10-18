#!/usr/bin/env node
import readline from 'node:readline';

const CANDIDATE_PORTS = [Number(process.env.REVIT_MCP_PORT) || 8080, 8081, 8082, 8085];
let activePort = CANDIDATE_PORTS[0];

async function forwardToRevit(rpcPayload) {
  for (const port of [activePort, ...CANDIDATE_PORTS.filter(p => p !== activePort)]) {
    try {
      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), 50000);
      const res = await fetch(`http://127.0.0.1:${port}/mcp/`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(rpcPayload),
        signal: controller.signal
      });
      clearTimeout(timeout);

      if (res.status === 202 || res.status === 204) {
        activePort = port;
        return null;
      }

      if (res.ok) {
        activePort = port;
        return await res.json();
      }
    } catch {
      // Probar el siguiente puerto si no responde
    }
  }
  return null;
}

const FALLBACK_TOOLS = [
  {
    name: "revit_get_active_session",
    description: "Obtiene el estado en vivo del documento abierto en Autodesk Revit.",
    inputSchema: { type: "object", properties: {} }
  },
  {
    name: "revit_get_selection",
    description: "Obtiene los elementos actualmente seleccionados en la pantalla de Revit.",
    inputSchema: { type: "object", properties: {} }
  },
  {
    name: "revit_select_elements",
    description: "Selecciona elementos en la interfaz gráfica de Revit por sus ElementIds y enfoca la vista.",
    inputSchema: {
      type: "object",
      properties: {
        element_ids: { type: "array", items: { type: "integer" } },
        zoom_to_elements: { type: "boolean" }
      },
      required: ["element_ids"]
    }
  },
  {
    name: "revit_query_elements",
    description: "Consulta y filtra elementos en vivo dentro del modelo de Revit por categoría, nivel o texto.",
    inputSchema: {
      type: "object",
      properties: {
        category: { type: "string" },
        level_name: { type: "string" },
        search_text: { type: "string" },
        limit: { type: "integer" }
      },
      required: ["category"]
    }
  },
  {
    name: "revit_get_levels_and_rooms",
    description: "Lista todos los niveles (Levels) y todas las habitaciones (Rooms) del modelo abierto.",
    inputSchema: { type: "object", properties: {} }
  },
  {
    name: "revit_get_materials",
    description: "Lista los materiales cargados en el proyecto activo de Revit.",
    inputSchema: {
      type: "object",
      properties: {
        search: { type: "string" },
        limit: { type: "integer" }
      }
    }
  },
  {
    name: "revit_modify_parameters",
    description: "Modifica parámetros de instancia o comentarios de uno o varios elementos en Revit.",
    inputSchema: {
      type: "object",
      properties: {
        element_ids: { type: "array", items: { type: "integer" } },
        parameters: { type: "object" }
      },
      required: ["element_ids", "parameters"]
    }
  },
  {
    name: "revit_manage_sheets",
    description: "Gestiona planos (Sheets) en Revit: listar, crear con TitleBlock clonado, o renumerar rangos en lote mediante doble pase anti-colisión.",
    inputSchema: {
      type: "object",
      properties: {
        action: { type: "string", enum: ["list", "create", "shift_range", "renumber", "batch_rename"] },
        sheet_number: { type: "string" },
        sheet_name: { type: "string" },
        template_sheet_id: { type: "integer" },
        title_block_type_id: { type: "integer" },
        clone_titleblock_from_sheet_id: { type: "integer" },
        start_number: { type: "integer" },
        end_number: { type: "integer" },
        offset: { type: "integer" },
        prefix: { type: "string" },
        renames: { type: "array" },
        new_sheets: { type: "array" }
      }
    }
  },
  {
    name: "revit_get_element_parameters",
    description: "Inspecciona en profundidad todos los parámetros de instancia y de tipo de un elemento en Revit.",
    inputSchema: {
      type: "object",
      properties: {
        element_id: { type: "integer" }
      },
      required: ["element_id"]
    }
  },
  {
    name: "revit_manage_views_and_viewports",
    description: "Gestiona vistas y su colocación en planos (Viewports) dentro de Revit.",
    inputSchema: {
      type: "object",
      properties: {
        action: { type: "string", enum: ["list", "duplicate", "add_to_sheet"] },
        view_id: { type: "integer" },
        view_type: { type: "string" },
        new_name: { type: "string" },
        duplicate_option: { type: "string", enum: ["simple", "with_detailing", "dependent"] },
        sheet_id: { type: "integer" },
        center_x_m: { type: "number" },
        center_y_m: { type: "number" }
      }
    }
  },
  {
    name: "revit_delete_or_change_type",
    description: "Elimina elementos del modelo o modifica su familia/tipo de forma segura dentro de Revit.",
    inputSchema: {
      type: "object",
      properties: {
        action: { type: "string", enum: ["delete", "change_type"] },
        element_ids: { type: "array", items: { type: "integer" } },
        new_type_id: { type: "integer" },
        new_type_name: { type: "string" }
      },
      required: ["element_ids"]
    }
  },
  {
    name: "revit_apply_template_config",
    description: "Aplica configuraciones masivas al proyecto mediante PlantillaApplicatorService.",
    inputSchema: {
      type: "object",
      properties: {
        config_json: { type: "string" }
      },
      required: ["config_json"]
    }
  },
  {
    name: "revit_convertir_pisos_3d",
    description: "Modela y parametriza el despiece de baldosas sobre un piso en Revit.",
    inputSchema: {
      type: "object",
      properties: {
        floor_id: { type: "integer" },
        material_baldosa: { type: "string" },
        longitud_cm: { type: "number" },
        ancho_cm: { type: "number" },
        espesor_cm: { type: "number" },
        junta_mm: { type: "number" },
        patron: { type: "string" }
      },
      required: ["material_baldosa"]
    }
  },
  {
    name: "revit_crear_tablas_computos",
    description: "Crea automáticamente las tablas de planificación (Schedules) de cómputos métricos en Revit.",
    inputSchema: {
      type: "object",
      properties: {
        especialidad: { type: "string" }
      }
    }
  },
  {
    name: "revit_sync_to_supabase",
    description: "Extrae toda la metadata BIM del documento activo en Revit y la sincroniza con Supabase.",
    inputSchema: {
      type: "object",
      properties: {
        include_3d_scene: { type: "boolean" }
      }
    }
  }
];

const rl = readline.createInterface({
  input: process.stdin,
  terminal: false
});

rl.on('line', async (line) => {
  const trimmed = line.trim();
  if (!trimmed) return;

  let req;
  try {
    req = JSON.parse(trimmed);
  } catch {
    return;
  }

  // Notificaciones sin id (e.g. notifications/initialized)
  if (req.id === undefined || req.id === null) {
    await forwardToRevit(req);
    return;
  }

  const revitRes = await forwardToRevit(req);
  if (revitRes) {
    process.stdout.write(JSON.stringify(revitRes) + '\n');
    return;
  }

  // Respuestas de fallback amigables si Revit aún no ha levantado el servidor
  if (req.method === 'initialize') {
    process.stdout.write(JSON.stringify({
      jsonrpc: "2.0",
      id: req.id,
      result: {
        protocolVersion: "2024-11-05",
        capabilities: {
          tools: { listChanged: false },
          resources: {},
          prompts: {}
        },
        serverInfo: {
          name: "review-plugin-mcp-bridge",
          version: "2.5.0"
        }
      }
    }) + '\n');
  } else if (req.method === 'tools/list') {
    process.stdout.write(JSON.stringify({
      jsonrpc: "2.0",
      id: req.id,
      result: {
        tools: FALLBACK_TOOLS
      }
    }) + '\n');
  } else {
    process.stdout.write(JSON.stringify({
      jsonrpc: "2.0",
      id: req.id,
      result: {
        content: [
          {
            type: "text",
            text: JSON.stringify({
              status: "offline",
              error: "Abre Autodesk Revit con reviewPlugIn activo para ejecutar esta herramienta."
            })
          }
        ],
        isError: true
      }
    }) + '\n');
  }
});
