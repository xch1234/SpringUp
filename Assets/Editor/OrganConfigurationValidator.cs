using System;
using System.Collections.Generic;
using SpringUp.Organs;
using UnityEditor;
using UnityEngine;

namespace SpringUp.EditorChecks
{
    public static class OrganConfigurationValidator
    {
        [MenuItem("Tools/SpringUp/检查/器官配置完整性")]
        public static void ValidateMenu()
        {
            try { Validate(); }
            catch (Exception error) { Debug.LogException(error); }
        }
        public static void Validate()
        {
            var issues = new List<string>();
            var ids = new Dictionary<string, string>();
            foreach (string guid in AssetDatabase.FindAssets("t:OrganConfig", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<OrganConfig>(path);
                try
                {
                    if (!config.TryCreateDefinition(out var definition, out var error))
                    { issues.Add(path + ": " + error); continue; }
                    if (ids.TryGetValue(definition.Id, out var previous))
                        issues.Add("重复 ID " + definition.Id + ": " + previous + " / " + path);
                    else ids.Add(definition.Id, path);
                }
                catch (Exception error) { issues.Add(path + ": " + error.Message); }
            }
            try
            {
                var definitions = OrganCatalog.LoadDefault().CreateDefinitions();
                var registered = new HashSet<string>();
                foreach (var definition in definitions) registered.Add(definition.Id);
                foreach (var id in ids.Keys)
                    if (!registered.Contains(id)) Debug.LogWarning("器官配置尚未加入默认目录：" + ids[id]);
            }
            catch (Exception error) { issues.Add("默认目录: " + error.Message); }
            if (issues.Count > 0) throw new InvalidOperationException("器官配置检查失败：\n" + string.Join("\n", issues));
            Debug.Log("[Organ Configuration] PASS: " + ids.Count + " unique configurations; parameters, references and default catalog.");
        }
    }
}
