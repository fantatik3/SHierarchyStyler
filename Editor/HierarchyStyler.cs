#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Hierarchy;
using Unity.Hierarchy.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

// Unity 6.6 (6000.6) notes
// ------------------------
// * The UI Toolkit based Hierarchy window is the default. It never invokes the IMGUI row callback, so rows are
//   styled through Unity.Hierarchy.Editor.HierarchyWindow.BindViewItem / UnbindViewItem instead.
// * The legacy Hierarchy (Edit > Project Settings > Editor > Hierarchy > Use Legacy Hierarchy) still works and is
//   driven through EditorApplication.hierarchyWindowItemByEntityIdOnGUI. The int based
//   hierarchyWindowItemOnGUI, InstanceIDToObject and GetInstanceID are obsolete and replaced by EntityId.
// * Both paths share the same store (ProjectSettings/HierarchyStyler.asset), so existing styles carry over.

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

    [FilePath("ProjectSettings/HierarchyStyler.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class HierarchyStyleStore : ScriptableSingleton<HierarchyStyleStore>
    {
        [FormerlySerializedAs("styles")]
        [SerializeField] internal List<HierarchyStyle> Styles = new List<HierarchyStyle>();
        public void Persist()
        {
            Save(true);
        }
    }

    /// <summary>Style lookup shared by the legacy (IMGUI) and new (UI Toolkit) Hierarchy renderers.</summary>
    [InitializeOnLoad]
    internal static class HierarchyStyleRenderer
    {
        private static readonly Dictionary<EntityId, HierarchyStyle> _objectStyles =
            new Dictionary<EntityId, HierarchyStyle>();
        private static readonly Dictionary<string, HierarchyStyle> _stylesById =
            new Dictionary<string, HierarchyStyle>();
        private static bool _indexReady;

        static HierarchyStyleRenderer()
        {
            // Legacy Hierarchy window (IMGUI).
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += LegacyHierarchyRows.Draw;

            // New Hierarchy window (UI Toolkit, default since Unity 6.6).
            HierarchyWindow.BindViewItem += ModernHierarchyRows.Bind;
            HierarchyWindow.UnbindViewItem += ModernHierarchyRows.Unbind;
            HierarchyWindow.PopulateContextMenu += ModernHierarchyRows.PopulateContextMenu;

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
            ModernHierarchyRows.RestyleAll();
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

            EntityId entityId = gameObject.GetEntityId();
            if (_objectStyles.TryGetValue(entityId, out var found)) return found;
            if (!_indexReady)
            {
                foreach (var style in HierarchyStyleStore.instance.Styles)
                    if (!string.IsNullOrEmpty(style.ObjectId)) _stylesById[style.ObjectId] = style;
                _indexReady = true;
            }
            string key = GlobalObjectId.GetGlobalObjectIdSlow(gameObject).ToString();
            _stylesById.TryGetValue(key, out found);
            _objectStyles[entityId] = found;
            return found;
        }

        /// <summary>The objects a row action should affect: the whole selection when the row is part of it.</summary>
        internal static List<GameObject> CollectTargets(GameObject gameObject)
        {
            var targets = new List<GameObject>();
            if (Selection.Contains(gameObject) && Selection.gameObjects.Length > 1)
            {
                foreach (var selected in Selection.gameObjects)
                    if (IsEligible(selected)) targets.Add(selected);
            }
            if (targets.Count == 0) targets.Add(gameObject);
            return targets;
        }

        internal static Texture2D EditIcon()
        {
            return EditorGUIUtility.IconContent(EditorGUIUtility.isProSkin ? "d_editicon.sml" : "editicon.sml").image
                as Texture2D;
        }
    }

    /// <summary>Row styling for the new UI Toolkit Hierarchy window (Unity 6.6 default).</summary>
    internal static class ModernHierarchyRows
    {
        private const float ButtonSize = 16f;
        private const float TintAlpha = 0.12f;

        private sealed class Decoration
        {
            public HierarchyViewItem Item;
            public HierarchyWindow Window;
            public GameObject Target;
            public VisualElement Tint;
            public VisualElement Stripe;
            public Button EditButton;
            public bool Hovered;
        }

        private static readonly Dictionary<HierarchyViewItem, Decoration> _decorations =
            new Dictionary<HierarchyViewItem, Decoration>();

        public static void Bind(HierarchyWindow window, HierarchyView view, HierarchyViewItem item)
        {
            var decoration = GetOrCreate(item);
            EnsureAttached(decoration);
            decoration.Window = window;
            decoration.Target = item.Handler is HierarchyGameObjectHandler handler
                ? handler.GetGameObject(item.Node)
                : null;
            Apply(decoration);
        }

        public static void Unbind(HierarchyWindow window, HierarchyView view, HierarchyViewItem item)
        {
            if (!_decorations.TryGetValue(item, out var decoration)) return;
            decoration.Target = null;
            decoration.Hovered = false;
            Apply(decoration);
        }

        public static void PopulateContextMenu(HierarchyWindow window, HierarchyView view, HierarchyViewItem item,
            DropdownMenu menu)
        {
            if (item == null || !(item.Handler is HierarchyGameObjectHandler handler)) return;
            var gameObject = handler.GetGameObject(item.Node);
            if (!HierarchyStyleRenderer.IsEligible(gameObject)) return;

            var targets = HierarchyStyleRenderer.CollectTargets(gameObject);
            Rect anchor = ToScreen(window, item.worldBound);
            menu.AppendSeparator();
            menu.AppendAction("Hierarchy Style...", action =>
            {
                if (action.eventInfo != null)
                    anchor = ToScreen(window, new Rect(action.eventInfo.mousePosition, Vector2.zero));
                HierarchyIconPicker.Open(anchor, targets);
            });
            if (HierarchyStyleRenderer.Find(gameObject) != null)
                menu.AppendAction("Remove Hierarchy Style", _ => HierarchyStyleCommands.Remove(targets));
        }

        /// <summary>Re-applies styles to every bound row. Called after the store changes.</summary>
        internal static void RestyleAll()
        {
            foreach (var decoration in _decorations.Values)
                if (decoration.Target != null) Apply(decoration);
        }

        private static Decoration GetOrCreate(HierarchyViewItem item)
        {
            if (_decorations.TryGetValue(item, out var existing)) return existing;

            var decoration = new Decoration { Item = item };

            decoration.Tint = new VisualElement { name = "hierarchy-styler-tint", pickingMode = PickingMode.Ignore };
            var tint = decoration.Tint.style;
            tint.position = Position.Absolute;
            tint.left = 0f; tint.right = 0f; tint.top = 0f; tint.bottom = 0f;
            tint.display = DisplayStyle.None;

            decoration.Stripe = new VisualElement { name = "hierarchy-styler-stripe", pickingMode = PickingMode.Ignore };
            var stripe = decoration.Stripe.style;
            stripe.position = Position.Absolute;
            stripe.left = 0f; stripe.top = 2f; stripe.bottom = 2f; stripe.width = 2f;
            stripe.display = DisplayStyle.None;

            var row = item.RowContainer ?? item;
            row.Insert(0, decoration.Tint);
            row.Insert(1, decoration.Stripe);

            decoration.EditButton = new Button(() => OpenPicker(decoration))
            {
                name = "hierarchy-styler-edit", tooltip = "Set hierarchy icon", text = ""
            };
            var button = decoration.EditButton.style;
            button.width = ButtonSize; button.height = ButtonSize;
            button.minWidth = ButtonSize; button.minHeight = ButtonSize;
            button.marginLeft = 2f; button.marginRight = 2f; button.marginTop = 0f; button.marginBottom = 0f;
            button.paddingLeft = 1f; button.paddingRight = 1f; button.paddingTop = 1f; button.paddingBottom = 1f;
            button.backgroundImage = new StyleBackground(HierarchyStyleRenderer.EditIcon());
            button.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            button.display = DisplayStyle.None;
            (item.RightCustomContainer ?? item).Add(decoration.EditButton);

            item.RegisterCallback<PointerEnterEvent>(_ => { decoration.Hovered = true; UpdateButton(decoration); });
            item.RegisterCallback<PointerLeaveEvent>(_ => { decoration.Hovered = false; UpdateButton(decoration); });

            _decorations[item] = decoration;
            return decoration;
        }

        /// <summary>Rows are recycled and Unity may clear the custom containers; re-add our elements if needed.</summary>
        private static void EnsureAttached(Decoration decoration)
        {
            var item = decoration.Item;
            var row = item.RowContainer ?? item;
            if (decoration.Tint.parent == null) row.Insert(0, decoration.Tint);
            if (decoration.Stripe.parent == null) row.Insert(Mathf.Min(1, row.childCount), decoration.Stripe);
            if (decoration.EditButton.parent == null) (item.RightCustomContainer ?? item).Add(decoration.EditButton);
        }

        private static void Apply(Decoration decoration)
        {
            var item = decoration.Item;
            var style = decoration.Target != null ? HierarchyStyleRenderer.Find(decoration.Target) : null;

            if (style != null && style.UseColor)
            {
                Color tint = style.RowColor;
                tint.a = TintAlpha;
                decoration.Tint.style.backgroundColor = tint;
                decoration.Tint.style.display = DisplayStyle.Flex;
                Color stripe = style.RowColor;
                stripe.a = 1f;
                decoration.Stripe.style.backgroundColor = stripe;
                decoration.Stripe.style.display = DisplayStyle.Flex;
            }
            else
            {
                decoration.Tint.style.display = DisplayStyle.None;
                decoration.Stripe.style.display = DisplayStyle.None;
            }

            var icon = style?.GetTexture() as Texture2D;
            if (icon != null && item.Icon != null)
            {
                item.Icon.style.backgroundImage = new StyleBackground(icon);
                item.Icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                item.Icon.style.unityBackgroundImageTintColor = decoration.Target.activeInHierarchy
                    ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            }
            else if (item.Icon != null)
            {
                item.Icon.style.backgroundImage = StyleKeyword.Null;
                item.Icon.style.backgroundSize = StyleKeyword.Null;
                item.Icon.style.unityBackgroundImageTintColor = StyleKeyword.Null;
            }

            UpdateButton(decoration);
        }

        private static void UpdateButton(Decoration decoration)
        {
            bool show = decoration.Hovered && HierarchyStyleRenderer.IsEligible(decoration.Target);
            decoration.EditButton.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void OpenPicker(Decoration decoration)
        {
            if (!HierarchyStyleRenderer.IsEligible(decoration.Target)) return;
            var targets = HierarchyStyleRenderer.CollectTargets(decoration.Target);
            HierarchyIconPicker.Open(ToScreen(decoration.Window, decoration.EditButton.worldBound), targets);
        }

        /// <summary>Converts panel coordinates of the Hierarchy window to screen coordinates.</summary>
        private static Rect ToScreen(EditorWindow window, Rect panelRect)
        {
            Vector2 origin = window != null ? window.position.position : Vector2.zero;
            return new Rect(origin + panelRect.position, panelRect.size);
        }
    }

    /// <summary>Row styling for the legacy IMGUI Hierarchy window.</summary>
    internal static class LegacyHierarchyRows
    {
        private const float ButtonSize = 16f;
        private static GUIContent _buttonContent;
        private static GUIStyle _buttonStyle;

        public static void Draw(EntityId entityId, Rect row)
        {
            var gameObject = EditorUtility.EntityIdToObject(entityId) as GameObject;
            if (gameObject == null) return;
            var style = HierarchyStyleRenderer.Find(gameObject);
            if (style != null && Event.current.type == EventType.Repaint) DrawStyle(entityId, gameObject, row, style);
            DrawQuickButton(gameObject, row);
        }

        private static void DrawStyle(EntityId entityId, GameObject gameObject, Rect row, HierarchyStyle style)
        {
            bool selected = Selection.Contains(entityId);
            if (style.UseColor)
            {
                if (!selected)
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
            if (!HierarchyStyleRenderer.IsEligible(gameObject)) return;
            if (!row.Contains(Event.current.mousePosition)) return;

            if (_buttonContent == null)
            {
                _buttonContent = new GUIContent(HierarchyStyleRenderer.EditIcon(), "Set hierarchy icon");
                _buttonStyle = new GUIStyle(EditorStyles.iconButton) { padding = new RectOffset(1, 1, 1, 1) };
            }

            float right = row.xMax - 2f;
            if (PrefabUtility.IsAnyPrefabInstanceRoot(gameObject)) right -= 18f; // keep clear of the prefab arrow
            var buttonRect = new Rect(right - ButtonSize, row.y + (row.height - ButtonSize) / 2f, ButtonSize, ButtonSize);

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(buttonRect, EditorGUIUtility.isProSkin
                    ? new Color(0.16f, 0.16f, 0.16f, 0.9f) : new Color(0.85f, 0.85f, 0.85f, 0.9f));

            if (!GUI.Button(buttonRect, _buttonContent, _buttonStyle)) return;

            HierarchyIconPicker.Open(GUIUtility.GUIToScreenRect(buttonRect),
                HierarchyStyleRenderer.CollectTargets(gameObject));
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
    /// Drop-down picker opened from a hierarchy row. Picking an icon or color applies to the target objects at once.
    /// Hosted in its own EditorWindow so it can be opened from both IMGUI and UI Toolkit callbacks.
    /// </summary>
    internal sealed class HierarchyIconPicker : EditorWindow
    {
        private const float RowHeight = 22f;
        private const float IconSize = 18f;
        private const float SwatchSize = 18f;
        private const string SearchControl = "HierarchyIconPickerSearch";
        private static readonly Vector2 WindowSize = new Vector2(280f, 480f);

        private static readonly Color[] Presets =
        {
            HierarchyStyleCommands.DefaultColor,
            new Color(0.40f, 0.80f, 0.45f), new Color(0.96f, 0.78f, 0.35f), new Color(0.96f, 0.55f, 0.35f),
            new Color(0.95f, 0.40f, 0.40f), new Color(0.75f, 0.50f, 0.95f), new Color(0.40f, 0.85f, 0.80f),
            new Color(0.95f, 0.50f, 0.72f), new Color(0.70f, 0.70f, 0.70f)
        };

        private List<GameObject> _targets = new List<GameObject>();
        private Texture2D _icon;
        private bool _useColor;
        private Color _color;
        private bool _hasStyle;
        private string _search = "";
        private Vector2 _scroll;
        private bool _focusSearch = true;
        private GUIStyle _rowStyle;
        private GUIStyle _headerStyle;

        /// <param name="screenAnchor">Screen-space rect the drop-down is attached to (button or mouse position).</param>
        public static void Open(Rect screenAnchor, List<GameObject> targets)
        {
            var window = CreateInstance<HierarchyIconPicker>();
            window.Init(targets);
            window.wantsMouseMove = true;
            window.ShowAsDropDown(screenAnchor, WindowSize);
        }

        private void Init(List<GameObject> targets)
        {
            _targets = targets ?? new List<GameObject>();
            var existing = _targets.Count > 0 ? HierarchyStyleRenderer.Find(_targets[0]) : null;
            _hasStyle = existing != null;
            _icon = existing?.GetTexture() as Texture2D;
            _useColor = existing == null || existing.UseColor;
            _color = existing != null ? existing.RowColor : HierarchyStyleCommands.DefaultColor;
        }

        private void OnGUI()
        {
            if (_rowStyle == null)
            {
                _rowStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft };
                _headerStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft };
            }
            if (Event.current.type == EventType.MouseMove) Repaint();
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
                GUIUtility.ExitGUI();
            }

            _targets.RemoveAll(target => target == null);
            if (_targets.Count == 0)
            {
                Close();
                GUIUtility.ExitGUI();
            }

            var rect = new Rect(0f, 0f, position.width, position.height);
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
                Close();
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
                Close();
        }

        /// <summary>Shows an error in the popup when the command fails. Returns true on success.</summary>
        private bool Commit(string error)
        {
            if (error == null)
            {
                _hasStyle = true;
                Repaint();
                return true;
            }
            Debug.LogWarning("[SHierarchy Styler] " + error);
            ShowNotification(new GUIContent(error));
            return false;
        }
    }
}
#endif
