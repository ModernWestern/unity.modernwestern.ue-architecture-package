using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Subsystems are created by reflection and save games are filled by the serializer, so code stripping (IL2CPP,
    /// Medium/High) could remove them, their constructors or their fields. On every build this keeps them whole.
    /// </summary>
    internal sealed class FrameworkLinkXml : IUnityLinkerProcessor
    {
        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            var path = Path.GetFullPath(Path.Combine("Library", "UEArchitecture", "Framework.link.xml"));

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Library");

            File.WriteAllText(path, Build());

            return path;
        }

        /// <summary>
        /// The link.xml content: every concrete subsystem and save game class, preserved whole.
        /// </summary>
        public static string Build()
        {
            var types = TypeCache.GetTypesDerivedFrom<Subsystem>()
                                 .Concat(TypeCache.GetTypesDerivedFrom<SaveGame>())
                                 .Where(type => !type.IsAbstract && !type.ContainsGenericParameters)
                                 .Distinct()
                                 .ToArray();

            var xml = new StringBuilder("<linker>\n");

            foreach (var assembly in types.GroupBy(type => type.Assembly.GetName().Name).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                // ignoreIfMissing: test or editor-only assemblies are not part of every build.
                xml.Append("  <assembly fullname=\"").Append(assembly.Key).Append("\" ignoreIfMissing=\"1\">\n");

                foreach (var type in assembly.OrderBy(type => type.FullName, StringComparer.Ordinal))
                {
                    xml.Append("    <type fullname=\"").Append(type.FullName?.Replace('+', '/')).Append("\" preserve=\"all\" />\n");
                }

                xml.Append("  </assembly>\n");
            }

            return xml.Append("</linker>\n").ToString();
        }

#pragma warning disable CS0618
        public void OnBeforeRun(BuildReport report, UnityLinkerBuildPipelineData data)
        {
        }

        public void OnAfterRun(BuildReport report, UnityLinkerBuildPipelineData data)
        {
        }
#pragma warning restore CS0618
    }
}
