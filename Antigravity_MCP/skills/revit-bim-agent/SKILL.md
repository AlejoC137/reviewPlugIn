---
name: revit-bim-agent
description: >-
  Capacidad autónoma para controlar Autodesk Revit en vivo mediante el MCP de reviewPlugIn.
  Permite consultar el modelo, crear ensamblajes (Assemblies) de puertas y ventanas por tipo o ejemplar,
  generar vistas de taller, modificar parámetros, gestionar láminas (Sheets) y tablas de planificación.
---

# Revit BIM Agent (reviewPlugIn MCP)

Este skill otorga a Antigravity y Claude la capacidad de inspeccionar, automatizar y modificar modelos BIM abiertos en Autodesk Revit mediante el servidor MCP local integrado en **reviewPlugIn**.

---

## 1. Verificación Inicial de Conexión
Antes de realizar modificaciones, verifica siempre la sesión activa de Revit:
1. Llama a `revit_get_active_session`.
2. Si responde `status: "success"`, confirma el nombre del proyecto, la vista activa (`active_view`) y si hay elementos seleccionados.
3. Si responde con error o que el servidor está apagado, pide al usuario abrir Revit con el plugin `reviewPlugIn`.

---

## 2. Catálogo de Herramientas y Casos de Uso

### A. Ensamblajes de Carpintería (Puertas y Ventanas)
* **Herramienta:** `revit_create_door_assemblies`
* **Parámetros:**
  - `group_by_instance` (bool): `false` para agrupar por Tipo; `true` para crear un ensamble por cada ejemplar individual en obra.
  - `single_instance_per_type` (bool): Si `group_by_instance` es `false`, `true` toma 1 ejemplar de muestra representativo para el ensamble (estándar carpintería de taller).
  - `initial_name` (string): Prefijo de partida (ej. `"P-01"` para puertas, `"V-01"` para ventanas).
  - `create_views` (bool): Si es `true`, genera automáticamente las vistas técnicas (Planta, Elevación Frontal, 3D Isométrico y Tabla de Despiece).
  - `category` (string): `"Doors"` o `"Windows"`.

### B. Consultas y Selección en Tiempo Real
* **`revit_get_selection`**: Devuelve los ElementIds actualmente seleccionados con el ratón por el usuario en la interfaz gráfica de Revit.
* **`revit_select_elements`**: Selecciona en pantalla elementos específicos por su ID y realiza zoom centrado sobre ellos.
* **`revit_query_elements`**: Filtra elementos por categoría (`"Doors"`, `"Walls"`, `"Floors"`, `"Windows"`), nivel o texto de búsqueda.
* **`revit_get_levels_and_rooms`**: Extrae todos los niveles y habitaciones configuradas en el modelo.
* **`revit_get_materials`**: Lista los materiales y acabados del proyecto activo.
* **`revit_get_element_parameters`**: Inspecciona en profundidad todos los parámetros de instancia y tipo de un elemento.

### C. Modificaciones y Parámetros
* **`revit_modify_parameters`**:
  - `element_ids`: Lista de IDs numéricos.
  - `parameters`: Diccionario clave-valor con los parámetros a actualizar (ej. `{"Comments": "Revisado por IA", "Marca": "P-10"}`).

### D. Gestión de Láminas (Sheets) y Vistas (Viewports)
* **`revit_manage_sheets`**:
  - `action`: `"list"`, `"create"`, `"shift_range"`, `"renumber"`, `"batch_rename"`.
  - Permite clonar TitleBlocks de planos existentes y renumerar rangos de planos evitando colisiones.
* **`revit_manage_views_and_viewports`**:
  - `action`: `"list"`, `"duplicate"`, `"add_to_sheet"`.
  - Duplica vistas con o sin detalles y las inserta en láminas con coordenadas precisas en metros.

### E. Modelado y Cómputos Avanzados
* **`revit_convertir_pisos_3d`**: Modela el despiece real de baldosas cerámicas sobre una losa de piso.
* **`revit_crear_tablas_computos`**: Crea tablas de planificación automáticas para albañilería, pisos o carpintería.
* **`revit_sync_to_supabase`**: Exporta y sincroniza la metadata completa del modelo con la nube de Supabase.

---

## 3. Reglas de Interacción con el Usuario
1. Cuando el usuario pida una acción que afecte a muchos elementos (como renumerar todas las láminas o modificar decenas de muros), reporta el conteo previo antes de ejecutar o confirma el resultado con un resumen estructurado.
2. Tras crear ensamblajes o vistas, reporta siempre la lista de nombres creados (`P-01`, `P-02`...) y las vistas generadas.
