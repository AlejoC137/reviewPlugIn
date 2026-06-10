# Guía de Conexión y Arquitectura

Este documento explica cómo funciona la conexión entre el plugin de Revit y tu ecosistema (Local Server MCP, React y Supabase), y cómo debes configurar las direcciones para que la información fluya correctamente.

## Arquitectura del Ecosistema

El flujo de información es unidireccional (Pull) en esta versión:

1. **Revit 2027 (Cliente HTTP):** El plugin se ejecuta dentro de Revit y realiza una petición HTTP GET al servidor local.
2. **Local Server (MCP / Puente):** Un servidor Node.js/Python que expone un endpoint local (ej. `http://localhost:3000/api/mcp-data`). Este servidor se encarga de hablar con Supabase y tu app en React.
3. **Supabase (Base de Datos):** Almacena y retorna los datos al Local Server.

## Cómo Configurar las Direcciones

La dirección a la que apunta el plugin está definida en el archivo `Command.cs`. 

Si necesitas cambiar la IP, el puerto, o la ruta de tu Local Server, debes modificar la siguiente línea en el archivo `Command.cs`:

```csharp
// Línea 24 en Command.cs
string localServerUrl = "http://localhost:3000/api/mcp-data";
```

*Nota:* Si vas a ejecutar Revit en una máquina diferente a donde corre tu Local Server, debes cambiar `localhost` por la dirección IP de la máquina de tu servidor (ej. `http://192.168.1.100:3000/api/mcp-data`).

## Formato de Datos Esperado

Para que el plugin de Revit pueda entender la información que viene del Local Server, tu servidor debe devolver un JSON estructurado como un arreglo de objetos. 

Basado en la clase `SupabaseDataModel` que viene definida al final del archivo `Command.cs`, el servidor debe devolver algo exactamente como esto:

```json
[
  {
    "id": "1",
    "name": "Muro Estructural",
    "status": "Aprobado"
  },
  {
    "id": "2",
    "name": "Losa de Cimentación",
    "status": "Pendiente"
  }
]
```

Si necesitas agregar más campos (como dimensiones, fechas, comentarios, etc.), debes:
1. Asegurarte de que tu Local Server envíe la nueva propiedad en la respuesta JSON.
2. Agregar la propiedad correspondiente en la clase `SupabaseDataModel` dentro de `Command.cs`.

## Ejemplo de Servidor Local (Node.js/Express)

Aquí tienes un ejemplo básico de cómo debería verse el código en tu servidor Node.js que expone los datos de Supabase para que Revit los lea:

```javascript
const express = require('express');
const app = express();
const port = 3000;

app.get('/api/mcp-data', async (req, res) => {
  try {
    // Aquí harías la consulta real a Supabase
    // const { data, error } = await supabase.from('elementos').select('*');
    
    // Datos mockeados de ejemplo para probar la conexión
    const data = [
      { id: "101", name: "Columna A1", status: "Instalada" },
      { id: "102", name: "Viga B2", status: "En tránsito" }
    ];

    // Enviar el JSON al plugin de Revit
    res.json(data);
  } catch (error) {
    res.status(500).json({ error: 'Error obteniendo datos' });
  }
});

app.listen(port, () => {
  console.log(`Servidor local corriendo en http://localhost:${port}`);
});
```
