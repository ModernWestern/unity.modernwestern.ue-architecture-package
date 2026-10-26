using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// Finds the classes the framework creates by itself (subsystems). Runs once per type at startup, never per frame.
    /// In the Editor it uses Unity's TypeCache; in players it scans only the assemblies that reference this package.
    /// The classes are kept in IL2CPP builds by a link.xml generated at build time.
    /// </summary>
    internal static class FrameworkTypes
    {
        private static readonly Dictionary<Type, Type[]> Cache = new();

        public static Type[] Concrete(Type baseType)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(baseType, out var types))
                {
                    return types;
                }

                var concrete = Find_Internal(baseType).Where(type => !type.IsAbstract && !type.ContainsGenericParameters).ToArray();

                foreach (var type in concrete.Where(type => type.GetConstructor(Type.EmptyTypes) == null))
                {
                    UnityEngine.Debug.LogWarning($"[UEArchitecture] {type.FullName} is skipped: subsystems need a public constructor without parameters" +
                                                 (UnityEngine.Application.isEditor ? "." : " (if it has one, code stripping removed it: keep the type with [Preserve] or a link.xml)."));
                }

                types = concrete.Where(type => type.GetConstructor(Type.EmptyTypes) != null)
                                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                                .ToArray();

                Cache[baseType] = types;

                return types;
            }
        }

        private static IEnumerable<Type> Find_Internal(Type baseType)
        {
#if UNITY_EDITOR
            return UnityEditor.TypeCache.GetTypesDerivedFrom(baseType);
#else
            var frameworkAssembly = baseType.Assembly;

            var frameworkName = frameworkAssembly.GetName().Name;

#if UNITY_6000_4_OR_NEWER
            return UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies()
#else
            return AppDomain.CurrentDomain.GetAssemblies()
#endif
                            .Where(assembly => assembly == frameworkAssembly || References_Internal(assembly, frameworkName))
                            .SelectMany(Types_Internal)
                            .Where(baseType.IsAssignableFrom);
#endif
        }

#if !UNITY_EDITOR
        private static bool References_Internal(Assembly assembly, string name)
        {
            try
            {
                return assembly.GetReferencedAssemblies().Any(reference => reference.Name == name);
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<Type> Types_Internal(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null);
            }
        }
#endif

        /// <summary>
        /// Forgets the cached lists (tests that add types at runtime, domain reload disabled).
        /// </summary>
        public static void Reset()
        {
            lock (Cache)
            {
                Cache.Clear();
            }
        }
    }
}
