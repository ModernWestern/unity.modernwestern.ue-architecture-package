using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ScriptCreator
{
    private const string TemplateRelativePath = "../Editor/Templates/{0}Template.txt";

    public static void CreateScriptAsset(string scriptName, string api = null, bool includeApi = true)
    {
        var fullPath = GetTemplateFullPath(scriptName);

        var apiName = (includeApi ? Application.productName : string.Empty)?.Replace(" ", string.Empty);

        if (!File.Exists(fullPath))
        {
            Debug.LogError($"Template not found at path: {fullPath}");
            return;
        }

        // Original template
        var template = File.ReadAllText(fullPath);

        // Replace the placeholders
        template = ReplacePlaceholders(template, api?.Replace(" ", string.Empty));
        
        // Temporal unity template
        var tempPath = Path.Combine(Path.GetTempPath(), $"{apiName}{scriptName}.cs.txt");
        
        File.WriteAllText(tempPath, template);

        // Asset creation
        ProjectWindowUtil.CreateScriptAssetFromTemplateFile(tempPath, $"{apiName}{scriptName}.cs");
    }

    private static string GetTemplateFullPath(string templateName)
    {
        var guid = AssetDatabase.FindAssets(nameof(ScriptCreator)).FirstOrDefault();

        if (string.IsNullOrEmpty(guid))
        {
            Debug.LogError("Could not locate ScriptCreator script in AssetDatabase.");
            return null;
        }

        var scriptPath = AssetDatabase.GUIDToAssetPath(guid);
        
        var rootDirectory = Path.GetDirectoryName(scriptPath);

        if (string.IsNullOrEmpty(rootDirectory))
        {
            Debug.LogError("Could not locate the path to the scripts folder.");
            return null;
        }

        var packageRoot = Path.GetFullPath(Path.Combine(rootDirectory, ".."));
        
        var fullTemplatePath = Path.Combine(packageRoot, string.Format(TemplateRelativePath, templateName));

        return fullTemplatePath.Replace("\\", "/");
    }

    private static string ReplacePlaceholders(string template, string api)
    {
        const string apiPlaceholder = "#API#";
        
        return template.Replace(apiPlaceholder, api ?? Application.productName);
    }
}

// Without custom placeholders
//
// using System.IO;
// using System.Linq;
// using UnityEditor;
// using UnityEngine;
//
// public static class ScriptCreator
// {
//     private const string TemplateRelativePath = "../Editor/Templates/{0}Template.txt";
//     
//     public static void CreateScriptAsset(string scriptName, string api = null)
//     {
//         var fullPath = GetTemplateFullPath(scriptName);
//
//         if (!File.Exists(fullPath))
//         {
//             Debug.LogError($"Template not found at path: {fullPath}");
//             return;
//         }
//
//         ProjectWindowUtil.CreateScriptAssetFromTemplateFile(fullPath, $"{Application.productName}{scriptName}.cs");
//     }
//
//     private static string GetTemplateFullPath(string templateName)
//     {
//         var guid = AssetDatabase.FindAssets(nameof(ScriptCreator)).FirstOrDefault();
//
//         if (string.IsNullOrEmpty(guid))
//         {
//             Debug.LogError("Could not locate ScriptCreator script in AssetDatabase.");
//             return null;
//         }
//
//         var scriptPath = AssetDatabase.GUIDToAssetPath(guid);
//         
//         var rootDirectory = Path.GetDirectoryName(scriptPath);
//
//         if (string.IsNullOrEmpty(rootDirectory))
//         {
//             Debug.LogError("Could not locate the path to the scripts folder.");
//             return null;
//         }
//         
//         var packageRoot = Path.GetFullPath(Path.Combine(rootDirectory, ".."));
//         
//         var fullTemplatePath = Path.Combine(packageRoot, string.Format(TemplateRelativePath, templateName));
//
//         return fullTemplatePath.Replace("\\", "/");
//     }
// }
