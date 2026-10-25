namespace ModernWestern.UEArchitecture.EditorTools
{
    public static partial class UEArchitectureCli
    {
        private const string SettingsHelp = "add-tag tag=<Name> | rename-tag from=<Name> to=<Name> [apply=true] (a preview, exit code 2, while it is used) | " +
                                            "remove-tag tag=<Name> [apply=true] (a preview, exit code 2, while it is used) | find-tag-usages tag=<Name> | " +
                                            "generate-tag-constants [path=Assets/...cs] [namespace=<Name>] [fix-references=true] | " +
                                            "list-levels | list-subsystems | add-to-build scene=<path> | set-first-level scene=<path> | ";

        /// <summary>
        /// The verbs of the settings page: gameplay tags, the levels list and the subsystems list, or null for another verb.
        /// </summary>
        private static ActionResult DispatchSettings_Internal(CliArguments arguments, CliSession session)
        {
            ActionResult failed;

            switch (arguments.Verb)
            {
                case "add-tag":
                    return Require_Internal(arguments, out failed, "tag") ? UEArchitectureActions.AddTag(arguments.Get("tag")) : failed;

                case "rename-tag":
                    return Require_Internal(arguments, out failed, "from", "to") ? UEArchitectureActions.RenameTag(arguments.Get("from"), arguments.Get("to"), arguments.Flag("apply")) : failed;

                case "remove-tag":
                    return Require_Internal(arguments, out failed, "tag") ? UEArchitectureActions.RemoveTag(arguments.Get("tag"), arguments.Flag("apply")) : failed;

                case "find-tag-usages":
                    return Require_Internal(arguments, out failed, "tag") ? UEArchitectureActions.FindTagUsages(arguments.Get("tag")) : failed;

                case "generate-tag-constants":
                    return UEArchitectureActions.GenerateTagConstants(arguments.Get("path"), arguments.Get("namespace"), arguments.Flag("fix-references"));

                case "list-levels":
                    return UEArchitectureActions.ListLevels();

                case "list-subsystems":
                    return UEArchitectureActions.ListSubsystems();

                case "add-to-build":
                    return Require_Internal(arguments, out failed, "scene") ? UEArchitectureActions.AddSceneToBuild(arguments.Get("scene")) : failed;

                case "set-first-level":
                    return Require_Internal(arguments, out failed, "scene") ? UEArchitectureActions.SetFirstLevel(arguments.Get("scene")) : failed;

                default:
                    return null;
            }
        }
    }
}
