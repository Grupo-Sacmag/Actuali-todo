using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Drawing;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Actuali_Todo
{
    public partial class Form1 : Form
    {
        private readonly string apiBaseUrl = "http://213.218.240.167:7777/api/versions";
        private readonly HttpClient httpClient;
        private VersionInfo versionServidorActual = null;

        // --- MONGODB Y TARIFAS ---
        private readonly string mongoConnectionString = "mongodb://localhost:29018";
        private readonly string mongoDatabaseName = "envarchdb";
        private readonly string mongoCollectionName = "files_metadata";
        private string rutaTarifasDestino = ""; 
        private List<MongoFileMetadata> tarifasServidorPendientes = new List<MongoFileMetadata>();

        // --- ÍNDICES Y DEPRECIACIONES ---
        private string rutaDepreciacionesDestino = "";
        private readonly string archivoConfigDeprec = "deprec_path.txt";
        private List<MongoFileMetadata> indicesServidorPendientes = new List<MongoFileMetadata>();

        // --- RUTAS DE INSTALACIÓN REALES ---
        private readonly Dictionary<string, string> rutasProgramas = new Dictionary<string, string>
        {
            { "ACUMULADO", @"C:\Program Files (x86)\AcuNom\Acumulado.exe" },
            { "AUXILIARES", @"C:\Program Files (x86)\Auxiliares\Auxiliares.exe" },
            { "CAPTURA", @"C:\Program Files (x86)\CAPTURA\Captura.exe" },
            { "CONTROLAVC", @"C:\Program Files (x86)\CltAv\CltAv.exe" },
            { "DEPRECIACION", @"C:\Program Files (x86)\DEPN\DEPREII.exe" },
            { "NOMINA", @"C:\Program Files (x86)\nomina1\Nomina.exe" }
        };
        // ------------------------------------

        public Form1()
        {
            InitializeComponent();
            httpClient = new HttpClient();
            btnBuscar.Enabled = false; 

            // Inicializar ruta de tarifas dinámicamente con el año en curso (ej. C:\TARIFA26)
            string anioDosDigitos = DateTime.Now.ToString("yy");
            rutaTarifasDestino = @$"C:\TARIFA{anioDosDigitos}";
        }

        // Clase para mapear el JSON de la API
        public class VersionInfo
        {
            [JsonPropertyName("Nombre")]
            public string Nombre { get; set; }

            [JsonPropertyName("Version")]
            public string Version { get; set; }

            [JsonPropertyName("exe")]
            public string Exe { get; set; }

            [JsonPropertyName("UltModif")]
            public DateTime UltModif { get; set; }
        }

        // Clase para mapear los metadatos de archivos guardados en MongoDB de G.A.C.
        [BsonIgnoreExtraElements]
        public class MongoFileMetadata
        {
            [BsonId]
            [BsonRepresentation(BsonType.ObjectId)]
            public string Id { get; set; }

            [BsonElement("fileName")]
            public string FileName { get; set; }

            [BsonElement("filePath")]
            public string FilePath { get; set; }

            [BsonElement("fileSize")]
            public long FileSize { get; set; }

            [BsonElement("contentType")]
            public string ContentType { get; set; }

            [BsonElement("userId")]
            public string UserId { get; set; }

            [BsonElement("uploadTime")]
            public DateTime UploadTime { get; set; }
        }

        private async Task<List<MongoFileMetadata>> ConsultarTarifasEnServidorAsync()
        {
            try
            {
                // Configurar un timeout corto de 3 segundos para evitar congelamientos si la BD local está apagada
                var settings = MongoClientSettings.FromConnectionString(mongoConnectionString);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(3);

                var client = new MongoClient(settings);
                var database = client.GetDatabase(mongoDatabaseName);
                var collection = database.GetCollection<MongoFileMetadata>(mongoCollectionName);

                // Buscamos documentos cuyos archivos terminen en .03, .cre, .isr, o .sub (insensible a mayúsculas)
                var filter = Builders<MongoFileMetadata>.Filter.Regex(
                    x => x.FileName,
                    new BsonRegularExpression(@"\.(03|cre|isr|sub)$", "i")
                );

                var archivos = await collection.Find(filter).ToListAsync();
                return archivos;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al conectar a MongoDB en {mongoConnectionString}: {ex.Message}");
                throw;
            }
        }

        private string ResolverRutaOrigenFisica(string filePathDeBD)
        {
            if (string.IsNullOrEmpty(filePathDeBD)) return null;

            if (File.Exists(filePathDeBD))
            {
                return filePathDeBD;
            }

            // Si no existe, podría ser porque corre en Docker y usa rutas de Linux (ej: /app/uploads/...)
            string nombreArchivoConUUID = Path.GetFileName(filePathDeBD);

            // 1. Intentamos buscarlo en la carpeta uploads de desarrollo local en Windows
            string rutaDesarrolloLocal = Path.Combine(@"C:\Users\david.albino\Proyectos\WEB\Monolito\Springboot\ComplementoActualitodo\uploads", nombreArchivoConUUID);
            if (File.Exists(rutaDesarrolloLocal))
            {
                return rutaDesarrolloLocal;
            }

            // 2. O buscarlo en la carpeta uploads relativa de envArch si es que corre ahí
            string rutaEnvArchLocal = Path.Combine(@"C:\Users\david.albino\Proyectos\WEB\Monolito\Springboot\ComplementoActualitodo\envArch\uploads", nombreArchivoConUUID);
            if (File.Exists(rutaEnvArchLocal))
            {
                return rutaEnvArchLocal;
            }

            // 3. Rutas de volúmenes de Docker Desktop en WSL2 (para volúmenes nombrados)
            string[] carpetasVolumenWSL = new string[]
            {
                @"\\wsl.localhost\docker-desktop-data\data\docker\volumes\complementoactualitodo_backend_uploads\_data",
                @"\\wsl.localhost\docker-desktop-data\data\docker\volumes\ComplementoActualitodo_backend_uploads\_data",
                @"\\wsl$\docker-desktop-data\data\docker\volumes\complementoactualitodo_backend_uploads\_data",
                @"\\wsl$\docker-desktop-data\data\docker\volumes\ComplementoActualitodo_backend_uploads\_data"
            };

            foreach (var carpetaVolumen in carpetasVolumenWSL)
            {
                string rutaVolumen = Path.Combine(carpetaVolumen, nombreArchivoConUUID);
                if (File.Exists(rutaVolumen))
                {
                    return rutaVolumen;
                }
            }

            return null;
        }

        private bool ObtenerOSeleccionarRutaDepreciaciones()
        {
            try
            {
                // Intentar leer la ruta desde el archivo de configuración deprec_path.txt
                if (File.Exists(archivoConfigDeprec))
                {
                    string rutaGuardada = File.ReadAllText(archivoConfigDeprec).Trim();
                    if (!string.IsNullOrEmpty(rutaGuardada) && Directory.Exists(rutaGuardada))
                    {
                        // Validar que la carpeta contenga el archivo maestro SOCTAS
                        string rutaMaestro = Path.Combine(rutaGuardada, "SOCTAS");
                        if (File.Exists(rutaMaestro))
                        {
                            rutaDepreciacionesDestino = rutaGuardada;
                            return true;
                        }
                    }
                }

                // Si no existe, no es válida o no tiene el maestro SOCTAS, pedir al usuario
                MessageBox.Show(
                    "Por favor, selecciona tu carpeta de depreciaciones contables. Recuerda que debe contener el archivo maestro 'SOCTAS' para ser válida.",
                    "Configuración de Depreciaciones",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
                {
                    folderDialog.Description = "Selecciona la carpeta de depreciaciones que contiene el archivo 'SOCTAS'.";
                    folderDialog.ShowNewFolderButton = false;

                    if (folderDialog.ShowDialog() == DialogResult.OK)
                    {
                        string rutaSeleccionada = folderDialog.SelectedPath;
                        string rutaMaestro = Path.Combine(rutaSeleccionada, "SOCTAS");

                        if (File.Exists(rutaMaestro))
                        {
                            rutaDepreciacionesDestino = rutaSeleccionada;
                            // Persistir la ruta en el archivo de texto
                            File.WriteAllText(archivoConfigDeprec, rutaSeleccionada);
                            return true;
                        }
                        else
                        {
                            MessageBox.Show(
                                "La carpeta seleccionada no es válida porque no contiene el archivo maestro 'SOCTAS'.",
                                "Carpeta Inválida",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al verificar o configurar la carpeta de depreciaciones: {ex.Message}", "Error de Configuración", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            rutaDepreciacionesDestino = "";
            return false;
        }

        private async Task VerificarIndicesDepreciacionAsync()
        {
            lblTarifaEstado.Text = "Índices: Verificando en base de datos...";
            lblTarifaEstado.ForeColor = Color.Blue;
            indicesServidorPendientes.Clear();

            // 1. Obtener/Validar carpeta de depreciaciones
            if (!ObtenerOSeleccionarRutaDepreciaciones())
            {
                lblTarifaEstado.Text = "Índices: Carpeta no configurada o inválida.";
                lblTarifaEstado.ForeColor = Color.Red;
                return;
            }

            try
            {
                // 2. Conectar a MongoDB y buscar índices (.10, .200, .94)
                var settings = MongoClientSettings.FromConnectionString(mongoConnectionString);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(3);

                var client = new MongoClient(settings);
                var database = client.GetDatabase(mongoDatabaseName);
                var collection = database.GetCollection<MongoFileMetadata>(mongoCollectionName);

                // Regex para extensiones de índices .10, .200, y .94
                var filter = Builders<MongoFileMetadata>.Filter.Regex(
                    x => x.FileName,
                    new BsonRegularExpression(@"\.(10|200|94)$", "i")
                );

                List<MongoFileMetadata> indicesServidor = await collection.Find(filter).ToListAsync();

                if (indicesServidor == null || indicesServidor.Count == 0)
                {
                    lblTarifaEstado.Text = "Índices: No se encontraron archivos en el servidor.";
                    lblTarifaEstado.ForeColor = Color.Gray;
                    return;
                }

                // 3. Comparar con archivos locales en la carpeta de depreciaciones
                foreach (var indice in indicesServidor)
                {
                    string rutaLocalArchivo = Path.Combine(rutaDepreciacionesDestino, indice.FileName);
                    bool pendiente = false;

                    if (!File.Exists(rutaLocalArchivo))
                    {
                        pendiente = true;
                    }
                    else
                    {
                        DateTime fechaLocal = File.GetLastWriteTime(rutaLocalArchivo);
                        DateTime fechaServidor = indice.UploadTime.ToLocalTime();

                        if (fechaServidor > fechaLocal.AddSeconds(2))
                        {
                            pendiente = true;
                        }
                    }

                    if (pendiente)
                    {
                        indicesServidorPendientes.Add(indice);
                    }
                }

                // 4. Mostrar estado final
                if (indicesServidorPendientes.Count > 0)
                {
                    lblTarifaEstado.Text = $"Índices: Desactualizados ({indicesServidorPendientes.Count} pendientes)";
                    lblTarifaEstado.ForeColor = Color.Red;
                    btnBuscar.Enabled = true; // Habilitar botón de actualización
                }
                else
                {
                    lblTarifaEstado.Text = "Índices: Al día.";
                    lblTarifaEstado.ForeColor = Color.Green;
                }
            }
            catch (Exception ex)
            {
                lblTarifaEstado.Text = $"Índices: Error al conectar a la BD ({ex.Message})";
                lblTarifaEstado.ForeColor = Color.OrangeRed;
            }
        }

        private async Task ActualizarIndicesLocalesAsync()
        {
            if (indicesServidorPendientes == null || indicesServidorPendientes.Count == 0) return;

            int actualizados = 0;
            progressBar1.Value = 0;
            progressBar1.Maximum = indicesServidorPendientes.Count;

            lblTarifaEstado.Text = "Índices: Actualizando...";
            lblTarifaEstado.ForeColor = Color.Blue;

            foreach (var indice in indicesServidorPendientes)
            {
                try
                {
                    string rutaOrigen = ResolverRutaOrigenFisica(indice.FilePath);
                    string rutaDestinoFinal = Path.Combine(rutaDepreciacionesDestino, indice.FileName);
                    bool copiadoExito = false;

                    // 1. Intentar copia física desde el host de Windows
                    if (!string.IsNullOrEmpty(rutaOrigen) && File.Exists(rutaOrigen))
                    {
                        using (FileStream sourceStream = new FileStream(rutaOrigen, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
                        using (FileStream destinationStream = new FileStream(rutaDestinoFinal, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                        {
                            await sourceStream.CopyToAsync(destinationStream);
                        }
                        copiadoExito = true;
                    }
                    // 2. Fallback usando docker cp desde el contenedor
                    else
                    {
                        copiadoExito = CopiarDesdeDockerContainer(indice.FilePath, rutaDestinoFinal);
                    }

                    if (!copiadoExito)
                    {
                        MessageBox.Show(
                            $"No se pudo encontrar el archivo físico ni copiar desde el contenedor de Docker para: {indice.FileName}\n\nRuta registrada en BD:\n{indice.FilePath}",
                            "Error de origen",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );
                        continue;
                    }

                    // Establecer LastWriteTime a la fecha del servidor
                    File.SetLastWriteTime(rutaDestinoFinal, indice.UploadTime.ToLocalTime());
                    actualizados++;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al copiar el archivo {indice.FileName}: {ex.Message}", "Error de copia", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                progressBar1.Value++;
            }

            MessageBox.Show(
                $"Se actualizaron con éxito {actualizados} archivos de índices en la carpeta '{rutaDepreciacionesDestino}'.",
                "Actualización exitosa",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );

            // Refrescar estado de los índices
            await VerificarIndicesDepreciacionAsync();
        }

        private async Task VerificarTarifasNominaAsync()
        {
            lblTarifaEstado.Text = "Tarifas: Verificando en base de datos...";
            lblTarifaEstado.ForeColor = Color.Blue;
            tarifasServidorPendientes.Clear();

            try
            {
                List<MongoFileMetadata> tarifasServidor = await ConsultarTarifasEnServidorAsync();
                if (tarifasServidor == null || tarifasServidor.Count == 0)
                {
                    lblTarifaEstado.Text = "Tarifas: No se encontraron archivos en el servidor.";
                    lblTarifaEstado.ForeColor = Color.Gray;
                    return;
                }

                bool carpetaExiste = Directory.Exists(rutaTarifasDestino);

                foreach (var tarifa in tarifasServidor)
                {
                    string rutaLocalArchivo = Path.Combine(rutaTarifasDestino, tarifa.FileName);
                    bool pendiente = false;

                    if (!carpetaExiste || !File.Exists(rutaLocalArchivo))
                    {
                        pendiente = true;
                    }
                    else
                    {
                        DateTime fechaLocal = File.GetLastWriteTime(rutaLocalArchivo);
                        DateTime fechaServidor = tarifa.UploadTime.ToLocalTime();

                        // Si el del servidor es más reciente
                        if (fechaServidor > fechaLocal.AddSeconds(2))
                        {
                            pendiente = true;
                        }
                    }

                    if (pendiente)
                    {
                        tarifasServidorPendientes.Add(tarifa);
                    }
                }

                if (tarifasServidorPendientes.Count > 0)
                {
                    lblTarifaEstado.Text = $"Tarifas: Desactualizadas ({tarifasServidorPendientes.Count} pendientes)";
                    lblTarifaEstado.ForeColor = Color.Red;
                    btnBuscar.Enabled = true; // Habilitamos botón de actualización
                }
                else
                {
                    lblTarifaEstado.Text = "Tarifas: Al día.";
                    lblTarifaEstado.ForeColor = Color.Green;
                }
            }
            catch (Exception ex)
            {
                lblTarifaEstado.Text = $"Tarifas: Error de conexión a la BD ({ex.Message})";
                lblTarifaEstado.ForeColor = Color.DarkGoldenrod;
            }
        }

        // ==================================================================
        // ===== ⬇️ MÉTODO MODIFICADO (AHORA ASÍNCRONO) ⬇️ =====
        // ==================================================================
        private async void comboBoxApps_SelectedIndexChanged(object sender, EventArgs e)
        {
            // --- 1. Resetear estado ---
            versionServidorActual = null;
            progressBar1.Value = 0;
            btnBuscar.Enabled = false; // <-- ¡NUEVO! Deshabilitar en cada cambio
            lblTarifaEstado.Text = "";
            lblTarifaEstado.ForeColor = SystemColors.ControlText;

            if (comboBoxApps.SelectedItem == null)
            {
                lblVersionLocal.Text = "Versión local: (Selecciona una app)";
                lblVersionServidor.Text = "Versión servidor: (Selecciona una app)"; // <-- NUEVO
                ActualizarImagen(null);
                return;
            }

            string app = comboBoxApps.SelectedItem.ToString();
            ActualizarImagen(app);

            // --- 2. Buscar ruta y leer versión local (Lógica existente) ---
            if (!rutasProgramas.TryGetValue(app, out string rutaCompletaExe))
            {
                lblVersionLocal.Text = "Versión local: (Ruta no definida)";
                lblVersionServidor.Text = "Versión servidor: (Ruta no definida)"; // <-- NUEVO
                return; // No podemos seguir
            }

            string versionLocal = LeerVersionLocal(app, rutaCompletaExe);
            lblVersionLocal.Text = $"Versión local: {versionLocal}";

            // --- 3. Consultar la API (Lógica movida aquí) ---
            try
            {
                lblVersionServidor.Text = "Conectando..."; // <-- NUEVO
                string apiUrl = $"{apiBaseUrl}/{app}";
                HttpResponseMessage response = await httpClient.GetAsync(apiUrl);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    lblVersionServidor.Text = $"No se encontró {app} en el servidor.";
                    return; // versionServidorActual sigue en null
                }
                else if (!response.IsSuccessStatusCode)
                {
                    lblVersionServidor.Text = $"Error del servidor: {(int)response.StatusCode}";
                    return; // versionServidorActual sigue en null
                }

                string respuesta = await response.Content.ReadAsStringAsync();
                var datos = JsonSerializer.Deserialize<VersionInfo>(respuesta);

                versionServidorActual = datos; // <-- ¡Guardamos los datos para el botón!

                // --- 4. ¡LÓGICA DE HABILITACIÓN! ---
                if (datos.Version != versionLocal)
                {
                    lblVersionServidor.Text = "¡Puedes actualizar!";
                    btnBuscar.Enabled = true; // <-- ¡HABILITADO!
                }
                else
                {
                    lblVersionServidor.Text = "Ya tienes la última versión.";
                    // btnBuscar.Enabled sigue siendo false
                }
            }
            catch (HttpRequestException)
            {
                lblVersionServidor.Text = "Error de conexión HTTP.";
            }
            catch (JsonException)
            {
                lblVersionServidor.Text = "Error al leer respuesta.";
            }
            catch (Exception ex)
            {
                lblVersionServidor.Text = $"Error: {ex.Message}";
            }

            // --- 5. Si es nómina, validar tarifas contables en MongoDB ---
            if (app == "NOMINA")
            {
                await VerificarTarifasNominaAsync();
            }

            // --- 6. Si es depreciación, validar índices en MongoDB ---
            if (app == "DEPRECIACION")
            {
                await VerificarIndicesDepreciacionAsync();
            }
        }

        // ==================================================================
        // ===== ⬇️ MÉTODO MODIFICADO (YA NO BUSCA, SOLO ACTÚA) ⬇️ =====
        // ==================================================================
        private async void btnBuscar_Click(object sender, EventArgs e)
        {
            string appSeleccionada = comboBoxApps.SelectedItem?.ToString();

            // Caso especial: NOMINA puede actualizar programa y/o tarifas
            if (appSeleccionada == "NOMINA")
            {
                bool actualizarPrograma = versionServidorActual != null;
                bool actualizarTarifas = tarifasServidorPendientes.Count > 0;
                if (actualizarPrograma && actualizarTarifas)
                {
                    var dialogBoth = MessageBox.Show(
                        $"Se encontró una nueva versión de la Nómina ({versionServidorActual.Version}) y además hay tarifas desactualizadas.\n\n¿Deseas actualizar ambas cosas?\n\n(Sí = Actualiza todo, No = Solo actualiza tarifas)",
                        "Actualizaciones disponibles",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Information
                    );

                    if (dialogBoth == DialogResult.Yes)
                    {
                        string rutaExeNominaAmbos = rutasProgramas[appSeleccionada];
                        btnBuscar.Enabled = false;
                        progressBar1.Value = 0;
                        lblVersionServidor.Text = "Iniciando descarga de Nómina...";
                        await DescargarYReemplazar(versionServidorActual, rutaExeNominaAmbos);
                        await ActualizarTarifasLocalesAsync();
                    }
                    else if (dialogBoth == DialogResult.No)
                    {
                        await ActualizarTarifasLocalesAsync();
                    }
                    return;
                }
                else if (actualizarPrograma)
                {
                    var dialog = MessageBox.Show(
                        $"Se encontró una nueva versión de {appSeleccionada} ({versionServidorActual.Version}). ¿Deseas actualizar el programa?",
                        "Actualización de Programa",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );

                    if (dialog == DialogResult.Yes)
                    {
                        string rutaExeNominaPrograma = rutasProgramas[appSeleccionada];
                        btnBuscar.Enabled = false;
                        progressBar1.Value = 0;
                        lblVersionServidor.Text = "Iniciando descarga...";
                        await DescargarYReemplazar(versionServidorActual, rutaExeNominaPrograma);
                    }
                    return;
                }
                else if (actualizarTarifas)
                {
                    var dialog = MessageBox.Show(
                        "Se detectaron tarifas desactualizadas en el servidor. ¿Deseas descargarlas e instalarlas?",
                        "Actualización de Tarifas",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );

                    if (dialog == DialogResult.Yes)
                    {
                        await ActualizarTarifasLocalesAsync();
                    }
                    return;
                }
            }

            // Caso especial: DEPRECIACION puede actualizar programa y/o índices
            if (appSeleccionada == "DEPRECIACION")
            {
                bool actualizarPrograma = versionServidorActual != null;
                bool actualizarIndices = indicesServidorPendientes.Count > 0;

                if (actualizarPrograma && actualizarIndices)
                {
                    var dialogBoth = MessageBox.Show(
                        $"Se encontró una nueva versión de la Depreciación ({versionServidorActual.Version}) y además hay índices desactualizados.\n\n¿Deseas actualizar ambas cosas?\n\n(Sí = Actualiza todo, No = Solo actualiza índices)",
                        "Actualizaciones disponibles",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Information
                    );

                    if (dialogBoth == DialogResult.Yes)
                    {
                        string rutaExeDeprecAmbos = rutasProgramas[appSeleccionada];
                        btnBuscar.Enabled = false;
                        progressBar1.Value = 0;
                        lblVersionServidor.Text = "Iniciando descarga de Depreciación...";
                        await DescargarYReemplazar(versionServidorActual, rutaExeDeprecAmbos);
                        await ActualizarIndicesLocalesAsync();
                    }
                    else if (dialogBoth == DialogResult.No)
                    {
                        await ActualizarIndicesLocalesAsync();
                    }
                    return;
                }
                else if (actualizarPrograma)
                {
                    var dialog = MessageBox.Show(
                        $"Se encontró una nueva versión de {appSeleccionada} ({versionServidorActual.Version}). ¿Deseas actualizar el programa?",
                        "Actualización de Programa",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );

                    if (dialog == DialogResult.Yes)
                    {
                        string rutaExeDeprecPrograma = rutasProgramas[appSeleccionada];
                        btnBuscar.Enabled = false;
                        progressBar1.Value = 0;
                        lblVersionServidor.Text = "Iniciando descarga...";
                        await DescargarYReemplazar(versionServidorActual, rutaExeDeprecPrograma);
                    }
                    return;
                }
                else if (actualizarIndices)
                {
                    var dialog = MessageBox.Show(
                        "Se detectaron índices de depreciación desactualizados en el servidor. ¿Deseas descargarlos e instalarlos?",
                        "Actualización de Índices",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );

                    if (dialog == DialogResult.Yes)
                    {
                        await ActualizarIndicesLocalesAsync();
                    }
                    return;
                }
            }

            // Flujo normal para otras aplicaciones (o si NOMINA solo tiene actualización de programa)
            if (versionServidorActual == null)
            {
                MessageBox.Show("Error inesperado. Vuelve a seleccionar la aplicación.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string app = versionServidorActual.Nombre;
            if (!rutasProgramas.TryGetValue(app, out string rutaCompletaExe))
            {
                lblVersionServidor.Text = $"No se ha definido una ruta de instalación para {app}.";
                return;
            }

            var dialogNormal = MessageBox.Show(
                $"Se encontró una nueva versión ({versionServidorActual.Version}). ¿Deseas actualizar?",
                "Actualización disponible",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information
            );

            if (dialogNormal == DialogResult.Yes)
            {
                btnBuscar.Enabled = false;
                progressBar1.Value = 0;
                lblVersionServidor.Text = "Iniciando descarga...";
                await DescargarYReemplazar(versionServidorActual, rutaCompletaExe);
            }
            else
            {
                lblVersionServidor.Text = "Actualización cancelada.";
            }
        }

        private async Task ActualizarTarifasLocalesAsync()
        {
            btnBuscar.Enabled = false;
            progressBar1.Value = 0;
            lblTarifaEstado.Text = "Tarifas: Actualizando...";
            lblTarifaEstado.ForeColor = Color.Blue;

            try
            {
                if (!Directory.Exists(rutaTarifasDestino))
                {
                    Directory.CreateDirectory(rutaTarifasDestino);
                }

                int total = tarifasServidorPendientes.Count;
                int procesados = 0;

                foreach (var tarifa in tarifasServidorPendientes)
                {
                    string rutaOrigen = ResolverRutaOrigenFisica(tarifa.FilePath);
                    string rutaDestino = Path.Combine(rutaTarifasDestino, tarifa.FileName);
                    bool copiadoExito = false;

                    // 1. Intentar copia física desde el host de Windows
                    if (!string.IsNullOrEmpty(rutaOrigen) && File.Exists(rutaOrigen))
                    {
                        using (FileStream SourceStream = File.Open(rutaOrigen, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            using (FileStream DestinationStream = File.Create(rutaDestino))
                            {
                                await SourceStream.CopyToAsync(DestinationStream);
                            }
                        }
                        copiadoExito = true;
                    }
                    // 2. Fallback usando docker cp desde el contenedor
                    else
                    {
                        copiadoExito = CopiarDesdeDockerContainer(tarifa.FilePath, rutaDestino);
                    }

                    if (!copiadoExito)
                    {
                        MessageBox.Show($"No se pudo encontrar el archivo físico ni copiar desde el contenedor de Docker para: {tarifa.FileName}\n\nRuta registrada en BD: {tarifa.FilePath}", "Error de origen", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        continue;
                    }

                    // Forzar fecha de modificación para que coincida
                    File.SetLastWriteTime(rutaDestino, tarifa.UploadTime.ToLocalTime());

                    procesados++;
                    progressBar1.Value = (int)(((double)procesados / total) * 100);
                }

                lblTarifaEstado.Text = "Tarifas: Al día.";
                lblTarifaEstado.ForeColor = Color.Green;
                progressBar1.Value = 100;

                MessageBox.Show($"Se actualizaron con éxito {procesados} archivos de tarifas en la carpeta '{rutaTarifasDestino}'.", "Actualización exitosa", MessageBoxButtons.OK, MessageBoxIcon.Information);
                tarifasServidorPendientes.Clear();
            }
            catch (Exception ex)
            {
                lblTarifaEstado.Text = $"Tarifas: Error al actualizar ({ex.Message})";
                lblTarifaEstado.ForeColor = Color.Red;
                MessageBox.Show($"Ocurrió un error al actualizar las tarifas: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnBuscar.Enabled = false;
            }
        }

        private bool CopiarDesdeDockerContainer(string containerPath, string destPath)
        {
            try
            {
                // Comando: docker cp backend_envarch:/app/uploads/uuid_filename.ext C:\Destino\filename.ext
                System.Diagnostics.ProcessStartInfo processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"cp backend_envarch:{containerPath} \"{destPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(processInfo))
                {
                    process.WaitForExit(5000); // Esperar hasta 5 segundos
                    if (process.ExitCode == 0)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Excepción al invocar docker cp: {ex.Message}");
            }
            return false;
        }

        private string LeerVersionLocal(string app, string rutaCompletaExe)
        {
            string directorio = Path.GetDirectoryName(rutaCompletaExe);
            string archivoVersion = Path.Combine(directorio, $"{app}_version.txt");

            if (!File.Exists(archivoVersion))
            {
                try
                {
                    // Intentamos crear el archivo de versión inicial
                    File.WriteAllText(archivoVersion, "1.0.0");
                    return "1.0.0"; // Devolvemos la versión que acabamos de crear
                }
                catch (Exception)
                {
                    // Esto fallará si no hay permisos de administrador
                    return "N/A (permisos)";
                }
            }

            try
            {
                // Si el archivo existe, lo leemos
                return File.ReadAllText(archivoVersion).Trim();
            }
            catch (Exception)
            {
                // Si no podemos leerlo por alguna razón
                return "Error al leer";
            }
        }

        private async Task DescargarYReemplazar(VersionInfo datos, string rutaCompletaExe)
        {
            try
            {
                lblVersionServidor.Text = "Descargando nueva versión...";
                string downloadUrl = $"{apiBaseUrl}/{datos.Nombre}/download";

                // Descarga a una carpeta temporal del sistema
                string rutaTemporal = Path.Combine(Path.GetTempPath(), datos.Exe);

                using (var client = new WebClient())
                {
                    client.DownloadProgressChanged += (s, e) =>
                    {
                        progressBar1.Value = e.ProgressPercentage;
                    };

                    await client.DownloadFileTaskAsync(new Uri(downloadUrl), rutaTemporal);
                }

                lblVersionServidor.Text = "Descarga completa. Reemplazando ejecutable...";

                // Mueve el archivo temporal a la ruta de instalación final
                ReemplazarEjecutable(datos, rutaTemporal, rutaCompletaExe);
            }
            catch (Exception ex)
            {
                lblVersionServidor.Text = $"Error al descargar: {ex.Message}";
            }
        }

        private void ReemplazarEjecutable(VersionInfo datos, string rutaTemporal, string rutaFinalExe)
        {
            // ... (toda la lógica de reemplazo) ...

            try // <-- AQUÍ ESTÁ EL CAMBIO (antes era solo un '{')
            {
                // rutaFinalExe = C:\Program Files (x86)\Auxiliares\Auxiliares.exe
                // rutaTemporal = C:\Users\TuUsuario\AppData\Local\Temp\Auxiliares.exe (el nuevo)

                string directorio = Path.GetDirectoryName(rutaFinalExe); // Ej: C:\Program Files (x86)\Auxiliares
                string oldFolderPath = Path.Combine(directorio, "old");  // Ej: C:\... \Auxiliares\old
                string nuevoExe = rutaTemporal;

                // 1. Verificamos si el ejecutable viejo existe para respaldarlo
                if (File.Exists(rutaFinalExe))
                {
                    // 2. Crear el directorio "old" (no hace nada si ya existe)
                    Directory.CreateDirectory(oldFolderPath);

                    // 3. Preparar el nombre del archivo de respaldo con fecha y hora
                    string fecha = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    string exeNombreSolo = Path.GetFileNameWithoutExtension(rutaFinalExe);
                    string exeExtension = Path.GetExtension(rutaFinalExe);
                    string nombreRespaldo = $"{exeNombreSolo}_{fecha}{exeExtension}"; // Ej: Auxiliares_2025-10-29_12-30-00.exe
                    string rutaRespaldo = Path.Combine(oldFolderPath, nombreRespaldo);

                    // 4. Mover el .exe viejo a la carpeta "old" con el nuevo nombre
                    File.Move(rutaFinalExe, rutaRespaldo);

                    lblVersionServidor.Text = "Respaldo de versión anterior creado.";
                }
                else
                {
                    lblVersionServidor.Text = "Instalando ejecutable...";
                }

                // 5. Mover el .exe nuevo (descargado en temp) a su lugar
                File.Move(nuevoExe, rutaFinalExe);

                lblVersionServidor.Text = "Actualización completada.";

                // 6. Guardar el TXT de versión en el mismo directorio final
                string archivoVersion = Path.Combine(directorio, $"{datos.Nombre}_version.txt");
                File.WriteAllText(archivoVersion, datos.Version);

                // 7. --- ¡NUEVA LÍNEA! ---
                // Actualizamos el label de la versión local AHORA,
                // para que refleje la nueva versión instalada.
                lblVersionLocal.Text = $"Versión local: {datos.Version}";
                btnBuscar.Enabled = false; // <-- ¡NUEVO! Deshabilitar después de actualizar


                MessageBox.Show($"La aplicación {datos.Nombre} fue actualizada a la versión {datos.Version}.", "Actualización exitosa", MessageBoxButtons.OK, MessageBoxIcon.Information);
            } // <-- ESTA LLAVE SE ELIMINÓ
            catch (Exception ex)
            {
                // Esto seguirá fallando si el programa está abierto o no eres admin
                lblVersionServidor.Text = $"Error al reemplazar archivo: {ex.Message}";
                MessageBox.Show($"Error: {ex.Message}\n\nAsegúrate de que el programa '{datos.Exe}' no esté abierto.\n\nRecuerda ejecutar el actualizador 'Como Administrador'.", "Error de reemplazo", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ==================================================================
        // ===== ⬇️ MÉTODO MODIFICADO CON "CHISMOSOS" ⬇️ =====
        // ==================================================================
        private void ActualizarImagen(string appName)
        {
            // Liberar la imagen anterior para evitar fugas de memoria
            pictureBox1.Image?.Dispose();

            // Ruta base donde se encuentra el .exe y la carpeta 'assets'
            string baseDir = Application.StartupPath;
            string assetsDir = Path.Combine(baseDir, "assets");
            string imagePath = "";

            // 1. Definir la ruta de la imagen
            if (!string.IsNullOrEmpty(appName))
            {
                // Asumimos que la imagen se llama igual que la app, con extensión .png
                // Ej: "NOMINA" -> "NOMINA.png"
                imagePath = Path.Combine(assetsDir, $"{appName}.png");

                // --- INICIO DE DEBUG ---
                //MessageBox.Show($"Intentando buscar la imagen específica:\n{imagePath}", "DEBUG 1");
                // --- FIN DE DEBUG ---
            }

            // 2. Si no hay app seleccionada o su imagen no existe, buscamos la default
            if (string.IsNullOrEmpty(appName) || !File.Exists(imagePath))
            {
                // --- INICIO DE DEBUG ---
                if (!string.IsNullOrEmpty(appName))
                {
                    // ESTE ES EL MENSAJE MÁS IMPORTANTE
                    // MessageBox.Show($"¡NO SE ENCONTRÓ!\nNo existe el archivo:\n{imagePath}\n\nCargando 'default.png' en su lugar.", "DEBUG 2 - ¡FALLO!");
                }
                // --- FIN DE DEBUG ---

                imagePath = Path.Combine(assetsDir, "default.png"); // Usa tu imagen por defecto
            }

            // 3. Cargar la imagen
            if (File.Exists(imagePath))
            {
                try
                {
                    // Cargamos la imagen leyéndola primero a un MemoryStream.
                    // Esto evita que el archivo .png quede "bloqueado" por el programa.
                    byte[] imageData = File.ReadAllBytes(imagePath);
                    using (var ms = new MemoryStream(imageData))
                    {
                        pictureBox1.Image = Image.FromStream(ms);
                    }
                }
                catch (Exception ex)
                {
                    pictureBox1.Image = null;
                    MessageBox.Show($"El archivo se encontró pero está corrupto:\n{imagePath}\nError: {ex.Message}", "DEBUG 3 - ¡ERROR!");
                }
            }
            else
            {
                // Si no existe ni la imagen de la app ni la default.png
                pictureBox1.Image = null;
            }
        }
        // ==================================================================
        // ===== ⬆️ FIN DEL NUEVO MÉTODO ⬆️ =====
        // ==================================================================


        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            httpClient?.Dispose();
            base.OnFormClosed(e);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Al cargar el formulario, muestra la imagen por defecto
            ActualizarImagen(null); // <-- LÍNEA AÑADIDA
        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {
            // Puedes añadir código aquí si quieres que pase algo al hacer clic
        }
    }
}