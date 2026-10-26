using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// Pawn setup where the user already is: the Pawn's gear menu (Make AI Pawn…, Add Path Following), a NavMeshAgent's
    /// gear menu (Add Path Following) and the Project window on several prefabs at once (UE Architecture > Add Path
    /// Following).
    /// </summary>
    internal static class PawnMenus
    {
        private const string PawnMenu = "CONTEXT/Pawn/";

        private const int Priority = 1500;

        [MenuItem(PawnMenu + "Make AI Pawn…", false, Priority)]
        private static void MakeAIPawn(MenuCommand command)
        {
            if (!(command.context is Pawn pawn))
            {
                return;
            }

            var classes = AIControllerClasses_Internal();

            if (classes.Count <= 1 || Application.isBatchMode)
            {
                Report_Internal(UEArchitectureActions.MakeAIPawn(pawn, classes.FirstOrDefault()));

                return;
            }

            var menu = new GenericMenu();

            foreach (var type in classes)
            {
                menu.AddItem(new GUIContent(UEArchitectureStyles.TypeName(type, classes)), false, () => Report_Internal(UEArchitectureActions.MakeAIPawn(pawn, type)));
            }

            menu.AddSeparator(string.Empty);

            menu.AddItem(new GUIContent("Plain AIController"), false, () => Report_Internal(UEArchitectureActions.MakeAIPawn(pawn, typeof(AIController))));

            menu.ShowAsContext();
        }

        [MenuItem(PawnMenu + "Make AI Pawn…", true)]
        private static bool MakeAIPawnValidate(MenuCommand command) => command.context is Pawn pawn && !ClassField.IsLocked(new Object[] { pawn });

        [MenuItem(PawnMenu + "Add Path Following", false, Priority + 1)]
        private static void AddPathFollowingToPawn(MenuCommand command)
        {
            Report_Internal(UEArchitectureActions.AddPathFollowing(new[] { command.context }));
        }

        [MenuItem(PawnMenu + "Add Path Following", true)]
        private static bool AddPathFollowingToPawnValidate(MenuCommand command) => Navigation && command.context is Pawn && !ClassField.IsLocked(new[] { command.context });

#if UEA_NAVIGATION
        [MenuItem("CONTEXT/NavMeshAgent/Add Path Following", false, Priority)]
        private static void AddPathFollowingToAgent(MenuCommand command)
        {
            Report_Internal(UEArchitectureActions.AddPathFollowing(new[] { command.context }));
        }

        [MenuItem("CONTEXT/NavMeshAgent/Add Path Following", true)]
        private static bool AddPathFollowingToAgentValidate(MenuCommand command) =>
            command.context is Component component && component.GetComponent<PathFollowing>() == null && !ClassField.IsLocked(new[] { command.context });
#endif

        [MenuItem("Assets/UE Architecture/Add Path Following", false, Priority + 30)]
        private static void AddPathFollowingToPrefabs()
        {
            Report_Internal(UEArchitectureActions.AddPathFollowing(NavigablePrefabs_Internal(Selection.objects).Cast<Object>()));
        }

        [MenuItem("Assets/UE Architecture/Add Path Following", true)]
        private static bool AddPathFollowingToPrefabsValidate() => Navigation && NavigablePrefabs_Internal(Selection.objects).Count > 0;

        /// <summary>
        /// The selected prefabs whose root is a Pawn or has a NavMeshAgent.
        /// </summary>
        internal static List<GameObject> NavigablePrefabs_Internal(IEnumerable<Object> selected)
        {
            return (selected ?? Array.Empty<Object>()).OfType<GameObject>()
                                                      .Where(prefab => EditorUtility.IsPersistent(prefab) && prefab.transform.parent == null &&
                                                                       (prefab.GetComponent<Pawn>() != null || prefab.GetComponents<Component>().Any(component => component != null && component.GetType().Name == "NavMeshAgent")))
                                                      .ToList();
        }

        private static bool Navigation
        {
            get
            {
#if UEA_NAVIGATION
                return true;
#else
                return false;
#endif
            }
        }

        private static List<Type> AIControllerClasses_Internal()
        {
            return TypeCache.GetTypesDerivedFrom<AIController>()
                            .Where(type => !type.IsAbstract && !type.ContainsGenericParameters && !UEArchitectureSettingsProvider.IsTestType_Internal(type))
                            .OrderBy(type => type.Name, StringComparer.Ordinal).ToList();
        }

        private static void Report_Internal(ActionResult result)
        {
            if (result.Ok)
            {
                LastCreatedFiles.Remember(result);

                Debug.Log("UE Architecture: " + result);
            }
            else
            {
                Debug.LogWarning("UE Architecture: " + string.Join("\n", result.Problems));
            }
        }
    }
}
