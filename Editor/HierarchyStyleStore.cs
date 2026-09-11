#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace CustomHierarchyTools
{
    /// <summary>
    /// Persisted style list. The durable copy is ProjectSettings/HierarchyStyler.json, written by this class with
    /// JsonUtility and re-read after every domain reload. A hidden ScriptableObject instance holds the working copy so
    /// Undo.RecordObject keeps working. ScriptableSingleton/FilePath is deliberately not used: Unity 6.6 loads that
    /// asset through class-identifier resolution that fails silently and then overwrites the file with an empty list.
    /// </summary>
    internal sealed class HierarchyStyleStore : ScriptableObject
    {
        [Serializable]
        private sealed class Data
        {
            public List<HierarchyStyle> Styles = new List<HierarchyStyle>();
        }

        [SerializeField] internal List<HierarchyStyle> Styles = new List<HierarchyStyle>();

        private static HierarchyStyleStore _instance;
        private static bool _diskFileUnreadable;

        public static string JsonPath => ProjectFile("HierarchyStyler.json");
        public static string LegacyAssetPath => ProjectFile("HierarchyStyler.asset");

        private static string ProjectFile(string fileName)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ProjectSettings", fileName));
        }

        public static HierarchyStyleStore instance
        {
            get
            {
                if (_instance != null) return _instance;
                // Reuse the object that survived the last domain reload so pending undo entries stay valid.
                foreach (var survivor in Resources.FindObjectsOfTypeAll<HierarchyStyleStore>())
                {
                    _instance = survivor;
                    break;
                }
                if (_instance == null)
                {
                    _instance = CreateInstance<HierarchyStyleStore>();
                    _instance.hideFlags = HideFlags.HideAndDontSave;
                }
                _instance.LoadFromDisk();
                return _instance;
            }
        }

        /// <summary>Discards the working copy and re-reads the file. Mostly for tests.</summary>
        internal static void ReloadFromDisk()
        {
            instance.LoadFromDisk();
        }

        private void LoadFromDisk()
        {
            _diskFileUnreadable = false;
            Styles.Clear();
            try
            {
                if (File.Exists(JsonPath))
                {
                    var data = JsonUtility.FromJson<Data>(File.ReadAllText(JsonPath));
                    if (data?.Styles != null) Styles.AddRange(data.Styles);
                }
                else if (File.Exists(LegacyAssetPath))
                {
                    Styles.AddRange(ReadLegacyAsset(LegacyAssetPath));
                    if (Styles.Count > 0)
                    {
                        Persist();
                        Debug.Log("[SHierarchy Styler] Migrated " + Styles.Count + " hierarchy style(s) from " +
                            LegacyAssetPath + " to " + JsonPath + ". The .asset file is no longer used and can be deleted.");
                    }
                }
            }
            catch (Exception exception)
            {
                _diskFileUnreadable = true;
                Debug.LogError("[SHierarchy Styler] Cannot read " + JsonPath + ": " + exception.Message +
                    "\nSaving is disabled so the file is not overwritten. Fix or delete the file and restart the editor.");
            }
        }

        public void Persist()
        {
            if (_diskFileUnreadable)
            {
                Debug.LogError("[SHierarchy Styler] " + JsonPath + " could not be read, so it is not overwritten. " +
                    "Fix or delete the file and restart the editor.");
                return;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(JsonPath));
                File.WriteAllText(JsonPath, JsonUtility.ToJson(new Data { Styles = Styles }, true));
            }
            catch (Exception exception)
            {
                Debug.LogError("[SHierarchy Styler] Cannot write " + JsonPath + ": " + exception.Message);
            }
        }

        /// <summary>Reads the style list from the YAML written by the previous ScriptableSingleton based store.</summary>
        private static List<HierarchyStyle> ReadLegacyAsset(string path)
        {
            var styles = new List<HierarchyStyle>();
            HierarchyStyle current = null;
            var colorPattern = new Regex(@"r:\s*([-\d.eE+]+),\s*g:\s*([-\d.eE+]+),\s*b:\s*([-\d.eE+]+),\s*a:\s*([-\d.eE+]+)");
            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("- ObjectId:", StringComparison.Ordinal))
                {
                    current = new HierarchyStyle { ObjectId = Value(line, "- ObjectId:") };
                    styles.Add(current);
                    continue;
                }
                if (current == null) continue;
                if (line.StartsWith("BuiltInIconId:", StringComparison.Ordinal)) current.BuiltInIconId = Value(line, "BuiltInIconId:");
                else if (line.StartsWith("TextureGuid:", StringComparison.Ordinal)) current.TextureGuid = Value(line, "TextureGuid:");
                else if (line.StartsWith("UseColor:", StringComparison.Ordinal)) current.UseColor = Value(line, "UseColor:") != "0";
                else if (line.StartsWith("RowColor:", StringComparison.Ordinal))
                {
                    var match = colorPattern.Match(line);
                    if (match.Success)
                        current.RowColor = new Color(Channel(match, 1), Channel(match, 2), Channel(match, 3), Channel(match, 4));
                }
            }
            styles.RemoveAll(style => string.IsNullOrEmpty(style.ObjectId));
            return styles;
        }

        private static string Value(string line, string key)
        {
            return line.Substring(key.Length).Trim();
        }

        private static float Channel(Match match, int group)
        {
            return float.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
        }
    }
}
#endif
