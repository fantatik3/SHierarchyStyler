#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Serialization;

namespace CustomHierarchyTools
{
    [Serializable]
    internal sealed class HierarchyStyle
    {
        [FormerlySerializedAs("objectId")]
        public string ObjectId;
        [FormerlySerializedAs("builtinIcon")]
        public string BuiltInIconId = "";
        [FormerlySerializedAs("textureGuid")]
        public string TextureGuid;
        [FormerlySerializedAs("useColor")]
        public bool UseColor = true;
        [FormerlySerializedAs("color")]
        public Color RowColor = HierarchyStyleCommands.DefaultColor;
        [NonSerialized] private Texture _cachedTexture;
        [NonSerialized] private bool _textureResolved;

        public Texture GetTexture()
        {
            if (_textureResolved) return _cachedTexture;
            _textureResolved = true;
            if (!string.IsNullOrEmpty(TextureGuid))
                _cachedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    AssetDatabase.GUIDToAssetPath(TextureGuid));
            else if (!string.IsNullOrEmpty(BuiltInIconId))
                _cachedTexture = BuiltInIconCatalog.ResolveTexture(BuiltInIconId);
            return _cachedTexture;
        }

        public void ClearTextureCache()
        {
            _textureResolved = false;
            _cachedTexture = null;
        }
    }

    /// <summary>Draws styled rows and the quick-assign button in the Hierarchy window.</summary>
    [InitializeOnLoad]
    internal static class HierarchyStyleRenderer
    {
        private const float ButtonSize = 16f;
        private static readonly Dictionary<int, HierarchyStyle> _objectStyles =
            new Dictionary<int, HierarchyStyle>();
        private static readonly Dictionary<string, HierarchyStyle> _stylesById =
            new Dictionary<string, HierarchyStyle>();
        private static bool _indexReady;
        private static GUIContent _buttonContent;
        private static GUIStyle _buttonStyle;

        static HierarchyStyleRenderer()
        {
            EditorApplication.hierarchyWindowItemOnGUI += DrawHierarchyItem;
            EditorApplication.hierarchyChanged += Refresh;
            EditorApplication.projectChanged += Refresh;
            EditorApplication.playModeStateChanged += _ => Refresh();
            EditorSceneManager.sceneSaved += _ => Refresh();
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private static void OnUndoRedo()
        {
            HierarchyStyleStore.instance.Persist();
            Refresh();
        }

        internal static void Refresh()
        {
            _objectStyles.Clear();
            _stylesById.Clear();
            _indexReady = false;
            foreach (var style in HierarchyStyleStore.instance.Styles)
                style.ClearTextureCache();
            EditorApplication.RepaintHierarchyWindow();
        }

        internal static bool IsEligible(GameObject gameObject)
        {
            return gameObject != null && !EditorUtility.IsPersistent(gameObject) && gameObject.scene.IsValid()
                && !string.IsNullOrEmpty(gameObject.scene.path)
                && PrefabStageUtility.GetPrefabStage(gameObject) == null;
        }

        internal static HierarchyStyle Find(GameObject gameObject)
        {
            if (!IsEligible(gameObject)) return null;

            int instanceId = gameObject.GetInstanceID();
            if (_objectStyles.TryGetValue(instanceId, out var found)) return found;
            if (!_indexReady)
            {
                foreach (var style in HierarchyStyleStore.instance.Styles)
                    if (!string.IsNullOrEmpty(style.ObjectId)) _stylesById[style.ObjectId] = style;
                _indexReady = true;
            }
            string key = GlobalObjectId.GetGlobalObjectIdSlow(gameObject).ToString();
            _stylesById.TryGetValue(key, out found);
            _objectStyles[instanceId] = found;
            return found;
        }

        private static void DrawHierarchyItem(int instanceId, Rect row)
        {
            var gameObject = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (gameObject == null) return;
            var style = Find(gameObject);
            if (style != null && Event.current.type == EventType.Repaint) DrawStyle(instanceId, gameObject, row, style);
            DrawQuickButton(gameObject, row);
        }

        private static void DrawStyle(int instanceId, GameObject gameObject, Rect row, HierarchyStyle style)
        {
            if (style.UseColor)
            {
                if (!Selection.Contains(instanceId))
                {
                    Color tint = style.RowColor;
                    tint.a = 0.12f;
                    EditorGUI.DrawRect(row, tint);
                }
                Color stripe = style.RowColor;
                stripe.a = 1f;
                EditorGUI.DrawRect(new Rect(row.x - 3f, row.y + 2f, 2f, row.height - 4f), stripe);
            }

            Texture icon = style.GetTexture();
            if (icon == null) return;

            var iconRect = new Rect(row.x, row.y + (row.height - 16f) / 2f, 16f, 16f);
            bool selected = Selection.Contains(instanceId);
            bool hierarchyFocused = EditorWindow.focusedWindow != null &&
                EditorWindow.focusedWindow.titleContent.text == "Hierarchy";
            Color background = EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.76f, 0.76f, 0.76f);
            if (selected)
                background = hierarchyFocused
                    ? new Color(0.24f, 0.48f, 0.70f)
                    : (EditorGUIUtility.isProSkin
                        ? new Color(0.30f, 0.30f, 0.30f) : new Color(0.68f, 0.68f, 0.68f));
            else if (style.UseColor)
                background = Color.Lerp(background, new Color(style.RowColor.r, style.RowColor.g, style.RowColor.b), 0.12f);
            EditorGUI.DrawRect(iconRect, background);
            Color previous = GUI.color;
            GUI.color = gameObject.activeInHierarchy ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
            GUI.color = previous;
        }

        /// <summary>Small pencil button at the right end of the hovered row. Opens the icon picker.</summary>
        private static void DrawQuickButton(GameObject gameObject, Rect row)
        {
            if (!IsEligible(gameObject)) return;
            if (!row.Contains(Event.current.mousePosition)) return;

            if (_buttonContent == null)
            {
                _buttonContent = EditorGUIUtility.IconContent(EditorGUIUtility.isProSkin ? "d_editicon.sml" : "editicon.sml");
                _buttonContent.tooltip = "Set hierarchy icon";
                _buttonStyle = new GUIStyle(EditorStyles.iconButton) { padding = new RectOffset(1, 1, 1, 1) };
            }

            float right = row.xMax - 2f;
            if (PrefabUtility.IsAnyPrefabInstanceRoot(gameObject)) right -= 18f; // keep clear of the prefab arrow
            var buttonRect = new Rect(right - ButtonSize, row.y + (row.height - ButtonSize) / 2f, ButtonSize, ButtonSize);

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(buttonRect, EditorGUIUtility.isProSkin
                    ? new Color(0.16f, 0.16f, 0.16f, 0.9f) : new Color(0.85f, 0.85f, 0.85f, 0.9f));

            if (!GUI.Button(buttonRect, _buttonContent, _buttonStyle)) return;

            var targets = new List<GameObject>();
            if (Selection.Contains(gameObject.GetInstanceID()) && Selection.gameObjects.Length > 1)
            {
                foreach (var selected in Selection.gameObjects)
                    if (IsEligible(selected)) targets.Add(selected);
            }
            if (targets.Count == 0) targets.Add(gameObject);
            PopupWindow.Show(buttonRect, new HierarchyIconPicker(targets));
        }
    }

    /// <summary>Applies, updates and removes styles. Shared by the picker.</summary>
    internal static class HierarchyStyleCommands
    {
        public static readonly Color DefaultColor = new Color(0.25f, 0.65f, 1f, 1f);

        /// <summary>Returns null when the object can be styled, otherwise a user-facing reason.</summary>
        public static string Validate(GameObject gameObject)
        {
            if (!HierarchyStyleRenderer.IsEligible(gameObject))
                return "Only objects in a saved scene can be styled. Prefab Mode and Project assets are not supported.";
            var id = GlobalObjectId.GetGlobalObjectIdSlow(gameObject);
            if (id.identifierType == 0 || id.targetObjectId == 0)
                return "'" + gameObject.name + "' has no saved ID yet. Save the scene (Ctrl+S) and try again.";
            return null;
        }

        public static string Apply(IList<GameObject> objects, Texture2D icon, bool useColor, Color color)
        {
            if (icon == null && !useColor) return Remove(objects);

            string guid = "";
            if (icon != null)
            {
                guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(icon));
                if (string.IsNullOrEmpty(guid)) return "The icon must be a texture asset inside the project.";
            }

            var keys = new List<string>();
            foreach (var gameObject in objects)
            {
                string error = Validate(gameObject);
                if (error != null) return error;
                keys.Add(GlobalObjectId.GetGlobalObjectIdSlow(gameObject).ToString());
            }

            var store = HierarchyStyleStore.instance;
            Undo.RecordObject(store, "Apply Hierarchy Style");
            foreach (string key in keys)
            {
                store.Styles.RemoveAll(style => style.ObjectId == key);
                store.Styles.Add(new HierarchyStyle
                {
                    ObjectId = key, BuiltInIconId = "", TextureGuid = guid, UseColor = useColor, RowColor = color
                });
            }
            store.Persist();
            HierarchyStyleRenderer.Refresh();
            return null;
        }

        public static string Remove(IList<GameObject> objects)
        {
            var keys = new List<string>();
            foreach (var gameObject in objects)
            {
                if (!HierarchyStyleRenderer.IsEligible(gameObject)) continue;
                keys.Add(GlobalObjectId.GetGlobalObjectIdSlow(gameObject).ToString());
            }
            if (keys.Count == 0) return "Nothing to remove.";

            var store = HierarchyStyleStore.instance;
            Undo.RecordObject(store, "Remove Hierarchy Style");
            foreach (string key in keys)
                store.Styles.RemoveAll(style => style.ObjectId == key);
            store.Persist();
            HierarchyStyleRenderer.Refresh();
            return null;
        }
    }

    internal static class BuiltInIconCatalog
    {
        private const string BundlePrefix = "bundle:";
        private const string TypePrefix = "type:";
        private static readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        private static AssetBundle _editorAssetBundle;
        private static bool _bundleResolved;

        internal static AssetBundle GetEditorAssetBundle()
        {
            if (_bundleResolved) return _editorAssetBundle;
            _bundleResolved = true;
            // Unity exposes the resource bundle through an internal API.
            var method = typeof(EditorGUIUtility).GetMethod("GetEditorAssetBundle",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (method != null)
                _editorAssetBundle = method.Invoke(null, null) as AssetBundle;
            return _editorAssetBundle;
        }

        public static Texture2D ResolveTexture(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_textures.TryGetValue(id, out var cached)) return cached;
            Texture2D texture = null;
            try
            {
                if (id.StartsWith(BundlePrefix, StringComparison.Ordinal))
                {
                    var bundle = GetEditorAssetBundle();
                    if (bundle != null)
                        texture = bundle.LoadAsset<Texture2D>(id.Substring(BundlePrefix.Length));
                }
                else if (id.StartsWith(TypePrefix, StringComparison.Ordinal))
                {
                    var type = Type.GetType(id.Substring(TypePrefix.Length), false);
                    if (type != null) texture = EditorGUIUtility.ObjectContent(null, type).image as Texture2D;
                }
                else
                    texture = EditorGUIUtility.FindTexture(id);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[SHierarchy Styler] Cannot load icon '" + id + "': " + exception.Message);
            }
            _textures[id] = texture;
            return texture;
        }
    }

    [InitializeOnLoad]
    internal static class HierarchyIconFolder
    {
        private const string FallbackRootPath = "Assets/Editor/SHierarchyIcons";
        private static string _rootPath;

        /// <summary>Icon folder next to this script ("Icons" or "SHierarchyIcons"), falling back to the default path.</summary>
        public static string RootPath
        {
            get
            {
                if (_rootPath == null) _rootPath = ResolveRootPath();
                return _rootPath;
            }
        }

        private static string ResolveRootPath()
        {
            foreach (string guid in AssetDatabase.FindAssets("HierarchyStyler t:Script"))
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(scriptPath) != "HierarchyStyler.cs") continue;
                string directory = Path.GetDirectoryName(scriptPath).Replace('\\', '/');
                foreach (string folder in new[] { "Icons", "SHierarchyIcons" })
                {
                    string candidate = directory + "/" + folder;
                    if (AssetDatabase.IsValidFolder(candidate)) return candidate;
                }
            }
            return FallbackRootPath;
        }

        internal sealed class Entry
        {
            public string Category;
            public string Name;
            public Texture2D Texture;
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static bool _isDirty = true;

        static HierarchyIconFolder()
        {
            EditorApplication.projectChanged += () => { _isDirty = true; _rootPath = null; };
        }

        internal static IReadOnlyList<Entry> GetEntries()
        {
            if (!_isDirty) return _entries;
            _entries.Clear();
            if (AssetDatabase.IsValidFolder(RootPath))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { RootPath }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (texture == null) continue;
                    string relative = path.Substring(RootPath.Length + 1);
                    string directory = Path.GetDirectoryName(relative);
                    _entries.Add(new Entry
                    {
                        Category = string.IsNullOrEmpty(directory) ? "Uncategorized" : directory.Replace('\\', '/'),
                        Name = Path.GetFileNameWithoutExtension(relative),
                        Texture = texture
                    });
                }
            }
            _entries.Sort((left, right) =>
            {
                int byCategory = string.Compare(left.Category, right.Category, StringComparison.OrdinalIgnoreCase);
                return byCategory != 0 ? byCategory
                    : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            });
            _isDirty = false;
            return _entries;
        }
    }

    /// <summary>
    /// Popup opened from the hierarchy row button. Picking an icon or color applies to the target objects at once.
    /// </summary>
    internal sealed class HierarchyIconPicker : PopupWindowContent
    {
        private const float RowHeight = 22f;
        private const float IconSize = 18f;
        private const float SwatchSize = 18f;
        private const string SearchControl = "HierarchyIconPickerSearch";

        private static readonly Color[] Presets =
        {
            HierarchyStyleCommands.DefaultColor,
            new Color(0.40f, 0.80f, 0.45f), new Color(0.96f, 0.78f, 0.35f), new Color(0.96f, 0.55f, 0.35f),
            new Color(0.95f, 0.40f, 0.40f), new Color(0.75f, 0.50f, 0.95f), new Color(0.40f, 0.85f, 0.80f),
            new Color(0.95f, 0.50f, 0.72f), new Color(0.70f, 0.70f, 0.70f)
        };

        private readonly List<GameObject> _targets;
        private Texture2D _icon;
        private bool _useColor;
        private Color _color;
        private bool _hasStyle;
        private string _search = "";
        private Vector2 _scroll;
        private bool _focusSearch = true;
        private GUIStyle _rowStyle;
        private GUIStyle _headerStyle;

        public HierarchyIconPicker(List<GameObject> targets)
        {
            _targets = targets;
            var existing = targets.Count > 0 ? HierarchyStyleRenderer.Find(targets[0]) : null;
            _hasStyle = existing != null;
            _icon = existing?.GetTexture() as Texture2D;
            _useColor = existing == null || existing.UseColor;
            _color = existing != null ? existing.RowColor : HierarchyStyleCommands.DefaultColor;
        }

        public override Vector2 GetWindowSize() => new Vector2(280f, 480f);

        public override void OnOpen()
        {
            editorWindow.wantsMouseMove = true;
        }

        public override void OnGUI(Rect rect)
        {
            if (_rowStyle == null)
            {
                _rowStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft };
                _headerStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft };
            }
            if (Event.current.type == EventType.MouseMove) editorWindow.Repaint();

            GUILayout.BeginArea(new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f));
            GUILayout.Label(_targets.Count == 1 ? _targets[0].name : _targets.Count + " objects", EditorStyles.boldLabel);

            DrawColorStrip();
            GUILayout.Space(4f);

            GUI.SetNextControlName(SearchControl);
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            if (_focusSearch)
            {
                EditorGUI.FocusTextInControl(SearchControl);
                _focusSearch = false;
            }
            GUILayout.Space(4f);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var entries = HierarchyIconFolder.GetEntries();
            if (string.IsNullOrEmpty(_search))
            {
                if (_hasStyle && DrawRow(null, "Remove style", false)) RemoveStyle();
                if (DrawRow(null, "No icon (color only)", _icon == null && _hasStyle)) ApplyIcon(null);
            }

            string currentCategory = null;
            int shown = 0;
            foreach (var entry in entries)
            {
                if (!Matches(entry)) continue;
                if (entry.Category != currentCategory)
                {
                    currentCategory = entry.Category;
                    GUILayout.Space(4f);
                    GUILayout.Label(currentCategory, _headerStyle);
                }
                if (DrawRow(entry.Texture, entry.Name, entry.Texture == _icon)) ApplyIcon(entry.Texture);
                shown++;
            }
            if (shown == 0)
                EditorGUILayout.HelpBox(entries.Count == 0
                    ? "Add PNG icons to " + HierarchyIconFolder.RootPath
                    : "No icons match the search.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawColorStrip()
        {
            Rect strip = GUILayoutUtility.GetRect(0f, SwatchSize + 4f, GUILayout.ExpandWidth(true));
            float x = strip.x;
            float y = strip.y + 2f;

            // "No color" swatch: outlined box with a diagonal line.
            var noneRect = new Rect(x, y, SwatchSize, SwatchSize);
            DrawSwatchFrame(noneRect, !_useColor);
            if (Event.current.type == EventType.Repaint)
            {
                var inner = new Rect(noneRect.x + 3f, noneRect.y + 3f, SwatchSize - 6f, SwatchSize - 6f);
                EditorGUI.DrawRect(inner, EditorGUIUtility.isProSkin ? new Color(0.3f, 0.3f, 0.3f) : new Color(0.8f, 0.8f, 0.8f));
                Handles.color = EditorGUIUtility.isProSkin ? new Color(0.85f, 0.35f, 0.35f) : new Color(0.7f, 0.1f, 0.1f);
                Handles.DrawLine(new Vector3(inner.x, inner.yMax), new Vector3(inner.xMax, inner.y));
            }
            if (ClickIn(noneRect)) ApplyColor(false, _color);
            x += SwatchSize + 4f;

            foreach (var preset in Presets)
            {
                var swatch = new Rect(x, y, SwatchSize, SwatchSize);
                bool active = _useColor && ColorsMatch(preset, _color);
                DrawSwatchFrame(swatch, active);
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(new Rect(swatch.x + 3f, swatch.y + 3f, SwatchSize - 6f, SwatchSize - 6f), preset);
                if (ClickIn(swatch)) ApplyColor(true, preset);
                x += SwatchSize + 4f;
            }
        }

        private static void DrawSwatchFrame(Rect rect, bool active)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color frame = active ? new Color(1f, 1f, 1f, 0.95f) : new Color(0f, 0f, 0f, 0.35f);
            EditorGUI.DrawRect(rect, frame);
            EditorGUI.DrawRect(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f),
                EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.9f, 0.9f, 0.9f));
        }

        private static bool ColorsMatch(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;
        }

        private static bool ClickIn(Rect rect)
        {
            if (Event.current.type != EventType.MouseDown || Event.current.button != 0) return false;
            if (!rect.Contains(Event.current.mousePosition)) return false;
            Event.current.Use();
            return true;
        }

        private bool Matches(HierarchyIconFolder.Entry entry)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            return entry.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                || entry.Category.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool DrawRow(Texture2D texture, string label, bool isSelected)
        {
            Rect row = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
            bool hover = row.Contains(Event.current.mousePosition);
            if (Event.current.type == EventType.Repaint)
            {
                if (isSelected) EditorGUI.DrawRect(row, new Color(0.24f, 0.48f, 0.70f, 0.55f));
                else if (hover) EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.08f));
                var iconRect = new Rect(row.x + 6f, row.y + (RowHeight - IconSize) / 2f, IconSize, IconSize);
                if (texture != null) GUI.DrawTexture(iconRect, texture, ScaleMode.ScaleToFit, true);
                var labelRect = new Rect(iconRect.xMax + 8f, row.y, row.width - iconRect.xMax - 8f, RowHeight);
                GUI.Label(labelRect, label, _rowStyle);
            }
            return hover && ClickIn(row);
        }

        private void ApplyIcon(Texture2D texture)
        {
            _icon = texture;
            if (Commit(HierarchyStyleCommands.Apply(_targets, _icon, _useColor, _color)))
                editorWindow.Close();
        }

        private void ApplyColor(bool useColor, Color color)
        {
            _useColor = useColor;
            _color = color;
            if (!_hasStyle && _icon == null && !useColor) return; // nothing to write yet
            Commit(HierarchyStyleCommands.Apply(_targets, _icon, _useColor, _color));
        }

        private void RemoveStyle()
        {
            if (Commit(HierarchyStyleCommands.Remove(_targets)))
                editorWindow.Close();
        }

        /// <summary>Shows an error in the popup when the command fails. Returns true on success.</summary>
        private bool Commit(string error)
        {
            if (error == null)
            {
                _hasStyle = true;
                editorWindow.Repaint();
                return true;
            }
            Debug.LogWarning("[SHierarchy Styler] " + error);
            editorWindow.ShowNotification(new GUIContent(error));
            return false;
        }
    }
}
#endif
