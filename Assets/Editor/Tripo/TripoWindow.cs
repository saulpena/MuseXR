using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MusePico.Gallery;
using MusePico.Tripo;
using UnityEditor;
using UnityEngine;

namespace MusePico.TripoEditor
{
    /// <summary>
    /// The Tripo console: MusePico &gt; Tripo.
    ///
    /// Three rules shape it.
    ///
    /// **Nothing bills without a click.** Every generation button states the floor price in
    /// credits and dollars and then asks for confirmation. Opening the window, refreshing the
    /// balance and checking a key all cost nothing. There is no auto-retry anywhere: a failed
    /// generation that silently retried would bill twice for one mistake.
    ///
    /// **The key is never displayed.** It is read from the environment inside the transport and
    /// is not held, shown, logged or written to the project. The window reports only whether one
    /// is present and how long it is.
    ///
    /// **A finished model lands as an asset, not a link.** Tripo's output URLs are pre-signed and
    /// expire, so a task is not done until the GLB is on disk, imported, and recorded in the
    /// catalogue with its provenance.
    /// </summary>
    public class TripoWindow : EditorWindow
    {
        const string GeneratedFolder = "Assets/Tripo/Generated";
        const string CatalogPreferenceKey = "MusePico.Tripo.Catalog";

        enum Mode { TextToModel, ImageToModel, Multiview }

        Mode _mode = Mode.TextToModel;
        bool _useChinaEndpoint;

        // Text to model
        string _prompt = "";
        string _negativePrompt = "";

        // Image / multiview
        Texture2D _image;
        readonly Texture2D[] _views = new Texture2D[4];
        static readonly string[] ViewNames = { "Front (required)", "Left", "Back", "Right" };

        // Shared options
        int _modelIndex;
        bool _texture = true;
        bool _pbr = true;
        int _textureQualityIndex;
        bool _limitFaces = true;
        int _faceLimit = 50000;
        bool _smartLowPoly;

        // Output
        string _assetName = "tripo-exhibit";
        GalleryCatalog _catalog;

        // Live state
        string _status = "";
        string _balance = "";
        string _taskId = "";
        bool _busy;
        Vector2 _scroll;
        CancellationTokenSource _cancel;

        static readonly string[] TextureQualities =
            { TripoApi.TextureQuality.Standard, TripoApi.TextureQuality.Detailed, TripoApi.TextureQuality.Extreme };

        [MenuItem("MusePico/Tripo")]
        public static void Open()
        {
            var window = GetWindow<TripoWindow>();
            window.titleContent = new GUIContent("Tripo");
            window.minSize = new Vector2(380, 520);
            window.Show();
        }

        void OnEnable()
        {
            var path = EditorPrefs.GetString(CatalogPreferenceKey, "");
            if (!string.IsNullOrEmpty(path)) _catalog = AssetDatabase.LoadAssetAtPath<GalleryCatalog>(path);
            _modelIndex = Array.IndexOf(TripoApi.Models.All, TripoApi.Models.H31);
            if (_modelIndex < 0) _modelIndex = 0;
        }

        void OnDisable() => _cancel?.Cancel();

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawCredentials();
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_busy))
            {
                DrawInput();
                EditorGUILayout.Space();
                DrawOptions();
                EditorGUILayout.Space();
                DrawOutput();
            }

            EditorGUILayout.Space();
            DrawActions();
            DrawStatus();

            EditorGUILayout.EndScrollView();
        }

        void DrawCredentials()
        {
            EditorGUILayout.LabelField("Credentials", EditorStyles.boldLabel);

            var transport = new TripoHttpTransport(serviceUrl: BaseUrl);
            if (transport.HasCredentials)
            {
                EditorGUILayout.HelpBox(TripoHttpTransport.DescribeKeySource(), MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    TripoHttpTransport.DescribeKeySource() + "\n\n" +
                    "PowerShell:  [Environment]::SetEnvironmentVariable('TRIPO_API_KEY','<key>','User')",
                    MessageType.Warning);
            }

            _useChinaEndpoint = EditorGUILayout.ToggleLeft(
                new GUIContent("Use the mainland-China endpoint",
                    "Accounts are region-bound. A key from tripo3d.com will 401 against tripo3d.ai."),
                _useChinaEndpoint);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_busy || !transport.HasCredentials))
                {
                    if (GUILayout.Button("Check balance (free)")) CheckBalance();
                }
                if (!string.IsNullOrEmpty(_balance)) EditorGUILayout.LabelField(_balance);
            }
        }

        void DrawInput()
        {
            EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);
            _mode = (Mode)EditorGUILayout.EnumPopup("Source", _mode);

            switch (_mode)
            {
                case Mode.TextToModel:
                    EditorGUILayout.LabelField("Prompt");
                    _prompt = EditorGUILayout.TextArea(_prompt, GUILayout.Height(54));
                    EditorGUILayout.LabelField("Negative prompt (optional)");
                    _negativePrompt = EditorGUILayout.TextField(_negativePrompt);
                    break;

                case Mode.ImageToModel:
                    _image = (Texture2D)EditorGUILayout.ObjectField("Image", _image, typeof(Texture2D), false);
                    EditorGUILayout.HelpBox(
                        "Uploaded to Tripo as a file_token, so it does not need to be published anywhere public.",
                        MessageType.None);
                    break;

                case Mode.Multiview:
                    for (var i = 0; i < 4; i++)
                        _views[i] = (Texture2D)EditorGUILayout.ObjectField(ViewNames[i], _views[i], typeof(Texture2D), false);
                    EditorGUILayout.HelpBox(
                        "Order is fixed: front, left, back, right. muse-infinity already crops its " +
                        "turnaround sheets into exactly this order under " +
                        "assets/generated/turnarounds/views/<character>/.",
                        MessageType.None);
                    break;
            }
        }

        void DrawOptions()
        {
            EditorGUILayout.LabelField("Generation", EditorStyles.boldLabel);

            _modelIndex = EditorGUILayout.Popup(
                new GUIContent("Model", "P1 is tuned for low-poly game assets; H3.1 for fidelity."),
                _modelIndex, TripoApi.Models.All);

            _texture = EditorGUILayout.Toggle("Texture", _texture);
            using (new EditorGUI.DisabledScope(!_texture))
            {
                _pbr = EditorGUILayout.Toggle("PBR maps", _pbr);
                _textureQualityIndex = EditorGUILayout.Popup("Texture quality", _textureQualityIndex, TextureQualities);
            }

            _smartLowPoly = EditorGUILayout.Toggle(
                new GUIContent("Smart low poly", "Clean hand-crafted-looking topology. Costs extra credits."),
                _smartLowPoly);

            _limitFaces = EditorGUILayout.Toggle(
                new GUIContent("Limit faces", "Strongly recommended. Tripo's defaults run to millions of triangles."),
                _limitFaces);
            using (new EditorGUI.DisabledScope(!_limitFaces))
                _faceLimit = EditorGUILayout.IntSlider("Face limit", _faceLimit, 5000, 300000);

            if (_limitFaces && _faceLimit > 120000)
                EditorGUILayout.HelpBox(
                    "Above ~120k triangles an exhibit starts to cost real frame time on a headset. " +
                    "The five Tripo characters in muse-infinity are ~2M each, which is why they " +
                    "have to be decimated before they can be shipped.",
                    MessageType.Warning);
        }

        void DrawOutput()
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            _assetName = EditorGUILayout.TextField(
                new GUIContent("Asset name", "Saved to " + GeneratedFolder + "/<name>.glb"), _assetName);

            var catalog = (GalleryCatalog)EditorGUILayout.ObjectField("Add to catalog", _catalog, typeof(GalleryCatalog), false);
            if (catalog != _catalog)
            {
                _catalog = catalog;
                EditorPrefs.SetString(CatalogPreferenceKey,
                    catalog == null ? "" : AssetDatabase.GetAssetPath(catalog));
            }
        }

        void DrawActions()
        {
            var transport = new TripoHttpTransport(serviceUrl: BaseUrl);
            var estimate = EstimateCredits();

            using (new EditorGUI.DisabledScope(_busy || !transport.HasCredentials))
            {
                var label = "Generate — about " + estimate + " credits (~$" +
                            (estimate * TripoApi.UsdPerCredit).ToString("0.00") + ")";
                if (GUILayout.Button(label, GUILayout.Height(30))) Generate();
            }

            if (_busy && GUILayout.Button("Cancel")) _cancel?.Cancel();

            if (!string.IsNullOrEmpty(_taskId))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Last task", _taskId);
                using (new EditorGUI.DisabledScope(_busy))
                    if (GUILayout.Button("Re-download this task's model"))
                        RedownloadLastTask();
            }
        }

        void DrawStatus()
        {
            if (string.IsNullOrEmpty(_status)) return;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(_status, _status.StartsWith("Failed") ? MessageType.Error : MessageType.Info);
        }

        int EstimateCredits()
        {
            var request = BuildRequest(validate: false);
            return request?.EstimatedCredits() ?? 0;
        }

        string BaseUrl => _useChinaEndpoint ? TripoApi.ChinaBaseUrl : TripoApi.GlobalBaseUrl;

        TripoModelRequest BuildRequest(bool validate)
        {
            TripoModelRequest request;
            switch (_mode)
            {
                case Mode.TextToModel:
                    request = new TextToModelRequest
                    {
                        Prompt = _prompt,
                        NegativePrompt = string.IsNullOrWhiteSpace(_negativePrompt) ? null : _negativePrompt,
                    };
                    if (validate && string.IsNullOrWhiteSpace(_prompt)) throw new InvalidOperationException("Enter a prompt.");
                    break;

                case Mode.ImageToModel:
                    if (validate && _image == null) throw new InvalidOperationException("Choose an image.");
                    request = new ImageToModelRequest { TextureAlignment = TripoApi.TextureAlignment.OriginalImage };
                    break;

                case Mode.Multiview:
                    if (validate && _views[0] == null) throw new InvalidOperationException("The front view is required.");
                    request = new MultiviewToModelRequest { TextureAlignment = TripoApi.TextureAlignment.OriginalImage };
                    break;

                default:
                    return null;
            }

            request.Model = TripoApi.Models.All[_modelIndex];
            request.Texture = _texture;
            request.Pbr = _texture && _pbr;
            if (_texture) request.TextureQuality = TextureQualities[_textureQualityIndex];
            if (_smartLowPoly) request.SmartLowPoly = true;
            if (_limitFaces) request.FaceLimit = _faceLimit;
            return request;
        }

        async void CheckBalance()
        {
            _busy = true;
            _status = "Checking balance…";
            Repaint();
            try
            {
                var client = new TripoClient(new TripoHttpTransport(serviceUrl: BaseUrl), BaseUrl);
                var balance = await client.GetBalanceAsync();
                _balance = balance.Describe();
                _status = "Key works. " + _balance;
            }
            catch (TripoException ex) { _status = "Failed: " + ex; }
            catch (Exception ex) { _status = "Failed: " + ex.Message; }
            finally { _busy = false; Repaint(); }
        }

        async void Generate()
        {
            TripoModelRequest request;
            try { request = BuildRequest(validate: true); }
            catch (Exception ex) { _status = "Failed: " + ex.Message; return; }

            var estimate = request.EstimatedCredits();
            var confirmed = EditorUtility.DisplayDialog(
                "Spend Tripo credits?",
                "This starts a billed generation.\n\n" +
                "Floor price: about " + estimate + " credits (~$" +
                (estimate * TripoApi.UsdPerCredit).ToString("0.00") + ").\n" +
                "Add-ons such as detailed texture or low-poly topology cost more.\n\n" +
                "Nothing is charged until you confirm.",
                "Generate", "Cancel");
            if (!confirmed) return;

            _cancel = new CancellationTokenSource();
            _busy = true;
            _status = "Preparing…";
            Repaint();

            try
            {
                var client = new TripoClient(new TripoHttpTransport(serviceUrl: BaseUrl), BaseUrl);
                await AttachImages(client, request, _cancel.Token);

                _status = "Submitting…";
                Repaint();
                _taskId = await client.CreateTaskAsync(request.Path, request.ToJson(), _cancel.Token);

                var progress = new Progress<TripoTask>(t => { _status = "Task " + t.Describe(); Repaint(); });
                var task = await client.WaitForTaskAsync(_taskId, progress, 2f, 900f, _cancel.Token);

                if (!task.IsSuccess)
                {
                    _status = "Failed: " + task.Describe();
                    return;
                }

                var url = task.output?.BestModelUrl;
                if (string.IsNullOrEmpty(url))
                {
                    _status = "Failed: the task succeeded but carried no model URL.";
                    return;
                }

                _status = "Downloading…";
                Repaint();
                var bytes = await client.DownloadAsync(url, _cancel.Token);

                var assetPath = SaveAndImport(bytes, _assetName);
                RecordInCatalog(assetPath, request, task);

                _status = "Done. " + assetPath + " — " + task.credits_consumed.ToString("0.##") + " credits used.";
            }
            catch (OperationCanceledException)
            {
                _status = "Cancelled locally. The task may still be running on Tripo's side and may still bill — " +
                          "task id " + _taskId + ".";
            }
            catch (TripoException ex) { _status = "Failed: " + ex; }
            catch (Exception ex) { _status = "Failed: " + ex.Message; }
            finally { _busy = false; _cancel = null; Repaint(); }
        }

        async Task AttachImages(TripoClient client, TripoModelRequest request, CancellationToken ct)
        {
            if (request is ImageToModelRequest image)
            {
                _status = "Uploading image…";
                Repaint();
                image.File = TripoFileRef.FromToken(await UploadTexture(client, _image, ct));
                return;
            }

            if (request is MultiviewToModelRequest multiview)
            {
                for (var i = 0; i < 4; i++)
                {
                    if (_views[i] == null) continue;
                    _status = "Uploading " + ViewNames[i] + "…";
                    Repaint();
                    multiview.Files[i] = TripoFileRef.FromToken(await UploadTexture(client, _views[i], ct));
                }
            }
        }

        /// <summary>
        /// Uploads a project texture by reading the FILE off disk, not by re-encoding the imported
        /// Texture2D. Unity's import pipeline may have resized, compressed or stripped the alpha
        /// of the asset in memory; Tripo should see what the artist made.
        /// </summary>
        static async Task<string> UploadTexture(TripoClient client, Texture2D texture, CancellationToken ct)
        {
            var path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new InvalidOperationException("'" + texture.name + "' is not a file on disk, so it cannot be uploaded.");

            var bytes = File.ReadAllBytes(path);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var contentType = extension == ".jpg" || extension == ".jpeg" ? "image/jpeg"
                : extension == ".webp" ? "image/webp"
                : "image/png";

            return await client.UploadFileAsync(bytes, Path.GetFileName(path), contentType, ct);
        }

        static string SaveAndImport(byte[] glb, string name)
        {
            Directory.CreateDirectory(GeneratedFolder);
            var safe = string.IsNullOrWhiteSpace(name) ? "tripo-exhibit" : name.Trim();
            foreach (var c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '-');

            var assetPath = AssetDatabase.GenerateUniqueAssetPath(GeneratedFolder + "/" + safe + ".glb");
            File.WriteAllBytes(assetPath, glb);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return assetPath;
        }

        void RecordInCatalog(string assetPath, TripoModelRequest request, TripoTask task)
        {
            if (_catalog == null) return;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (model == null)
            {
                _status += "\nImported, but glTFast produced no GameObject — check the console.";
                return;
            }

            var source = request is TextToModelRequest text ? text.Prompt
                : request is ImageToModelRequest ? "image: " + AssetDatabase.GetAssetPath(_image)
                : "multiview: front/left/back/right from the project";

            Undo.RecordObject(_catalog, "Add Tripo exhibit");
            _catalog.entries.Add(new GalleryEntry
            {
                id = Path.GetFileNameWithoutExtension(assetPath),
                displayName = Path.GetFileNameWithoutExtension(assetPath),
                caption = "",
                model = model,
                tripoModelVersion = request.Model,
                tripoTaskType = request.Path.Replace("/generation/", "").Replace("-", "_"),
                tripoTaskId = task.task_id,
                sourceInputs = source,
                rights = "Generated with Tripo. If this depicts a person, it is an AI interpretation, " +
                         "not an authentic likeness.",
                generatedOn = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            });
            EditorUtility.SetDirty(_catalog);
            AssetDatabase.SaveAssets();
        }

        async void RedownloadLastTask()
        {
            _busy = true;
            _status = "Fetching task…";
            Repaint();
            try
            {
                var client = new TripoClient(new TripoHttpTransport(serviceUrl: BaseUrl), BaseUrl);
                var task = await client.GetTaskAsync(_taskId);
                var url = task.output?.BestModelUrl;
                if (string.IsNullOrEmpty(url)) { _status = "Failed: " + task.Describe(); return; }

                var bytes = await client.DownloadAsync(url);
                _status = "Done. " + SaveAndImport(bytes, _assetName);
            }
            catch (TripoException ex) { _status = "Failed: " + ex; }
            catch (Exception ex) { _status = "Failed: " + ex.Message; }
            finally { _busy = false; Repaint(); }
        }
    }
}
