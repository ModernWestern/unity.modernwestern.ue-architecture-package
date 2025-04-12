using System;
using UnityEngine;
using System.Linq;
using UnityEditor;
using UEArchitecture;
using System.Collections.Generic;
using BindingFlags = System.Reflection.BindingFlags;

[CustomEditor(typeof(GameEvents), true), CanEditMultipleObjects]
public class GameEventsEditor : Editor
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
    
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(10);
        
        EditorGUILayout.LabelField("Events", TitleStyle());

        GUILayout.Space(10);
        
        var type = target.GetType();

        var fields = new List<System.Reflection.FieldInfo>();
        
        while (type != null && type != typeof(object))
        {
            fields.AddRange(type.GetFields(Flags | BindingFlags.DeclaredOnly));
            
            type = type.BaseType;
        }
        
        foreach (var field in fields)
        {
            var fieldType = field.FieldType;

            if (fieldType == typeof(Action))
            {
                EditorGUILayout.LabelField(field.Name, BoxStyle());
            }
            else if (fieldType.IsGenericType)
            {
                var genericArgs = fieldType.GetGenericArguments();

                var argsString = string.Join(", ", genericArgs.Select(GetTypeAlias));

                EditorGUILayout.LabelField($"{field.Name} ({argsString})", BoxStyle());
            }
        }
    }

    private static string GetTypeAlias(Type type)
    {
        return type switch
        {
            _ when type == typeof(int) => "Int",
            _ when type == typeof(float) => "Float",
            _ when type.IsGenericType && GenericTypes("System.Func") => "Func",
            _ when type.IsGenericType && GenericTypes("System.Action") => "Action",
            _ when type.IsGenericType && GenericTypes("System.Collections.Generic.List") => "List",
            _ when type.IsGenericType && GenericTypes("System.Collections.Generic.Queue") => "Queue",
            _ when type.IsGenericType && GenericTypes("System.Collections.Generic.Stack") => "Stack",
            _ => type.Name
        };

        bool GenericTypes(string @namespace)
        {
            var genericDef = type.GetGenericTypeDefinition();

            return genericDef.FullName != null && genericDef.FullName.StartsWith(@namespace);
        }
    }
    
    private static GUIStyle TitleStyle()
    {
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            richText = true,
            padding = new RectOffset(0, 0, 0, 0)
        };
        
        return style;
    }
    
    private static GUIStyle BoxStyle()
    {
        var style = new GUIStyle(EditorStyles.helpBox)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            richText = true,
            padding = new RectOffset(6, 6, 6, 6)
        };
        
        return style;
    }
}