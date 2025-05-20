using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using System.Linq;
using System.Reflection;

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

        var template = File.ReadAllText(fullPath);

        template = ReplacePlaceholders(template, api?.Replace(" ", string.Empty));

        var tempPath = Path.Combine(Path.GetTempPath(), $"{apiName}{scriptName}.cs.txt");

        File.WriteAllText(tempPath, template);

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

        template = template.Replace(apiPlaceholder, api ?? Application.productName);

        var rootNamespace = GetRootNamespace();

        if (string.IsNullOrWhiteSpace(rootNamespace))
        {
            return template;
        }

        SplitUsingsAndBody(template, out var usings, out var body);

        var indentedBody = IndentTemplate(body);

        template = $"{usings}\n\nnamespace {rootNamespace}\n{{\n{indentedBody}\n}}";

        return template;
    }

    private static void SplitUsingsAndBody(string template, out string usings, out string body)
    {
        var lines = template.Split(new[]
        {
            "\r\n", "\r", "\n"

        }, StringSplitOptions.None);

        var usingLines = new List<string>();

        var bodyLines = new List<string>();

        var pastUsings = false;

        foreach (var line in lines)
        {
            if (!pastUsings && line.TrimStart().StartsWith("using"))
            {
                usingLines.Add(line);
            }
            else
            {
                pastUsings = true;
                bodyLines.Add(line);
            }
        }

        usings = string.Join("\n", usingLines);

        body = string.Join("\n", bodyLines).Trim('\n', '\r');
    }


    private static string IndentTemplate(string content, int indentLevel = 1)
    {
        var indent = new string(' ', indentLevel * 4);

        var lines = content.Split(new[]
        {
            "\r\n", "\r", "\n"

        }, StringSplitOptions.None);

        var indentedLines = lines.Select(line => string.IsNullOrWhiteSpace(line) ? line : indent + line);

        return string.Join("\n", indentedLines);
    }

    private static string GetRootNamespace()
    {
        var editorSettingsType = typeof(EditorSettings);

        var property = editorSettingsType.GetProperty("projectGenerationRootNamespace", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);

        if (property != null)
        {
            return property.GetValue(null) as string;
        }

        return null;
    }
}
