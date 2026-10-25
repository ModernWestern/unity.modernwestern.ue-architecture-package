using System;
using System.Linq;
using UnityEngine;
using System.Reflection;
using ModernWestern.UI.ObserverVM;
using Object = UnityEngine.Object;

namespace ModernWestern.UEArchitecture.EditorTools
{
    /// <summary>
    /// The integration's command-line verbs, added to "-uea": predict-binding, add-framework-binder(s), hud-widgets,
    /// add-hud-widget and create-widget-for.
    /// </summary>
    internal static class FrameworkWidgetCli
    {
        public const string Help = "predict-binding target=<widget address> [component=<Class>] | add-framework-binders target=<address> [dry-run=true] [apply-to=instance|prefab] | " +
                                   "add-framework-binder target=<widget address> [player=<n>] [apply-to=instance|prefab] | hud-widgets target=<HUD address> [preview=<GameMode prefab>] [strict=true] | " +
                                   "add-hud-widget target=<HUD address> view-model=<Class> [widget=<Class>] [folder=Assets/...] | " +
                                   "create-widget-for class=<Class> placement=hud|canvas|prefab [target=<address>] [folder=Assets/...]";

        internal static void Register_Internal()
        {
            UEArchitectureCli.RegisterVerbs_Internal(Help, Dispatch_Internal);
        }

        internal static ActionResult Dispatch_Internal(CliArguments arguments, CliSession session)
        {
            ActionResult failed;

            switch (arguments.Verb)
            {
                case "predict-binding":
                {
                    if (!UEArchitectureCli.Require_Internal(arguments, out failed, "target"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var widget = Widget_Internal(arguments, session, result);

                    return result.Ok ? FrameworkWidgetActions.PredictBinding(widget) : result;
                }

                case "add-framework-binders":
                {
                    if (!UEArchitectureCli.Require_Internal(arguments, out failed, "target"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var found = session.Resolve(arguments.Get("target"), result);

                    var root = found is Component component ? component.gameObject : found as GameObject;

                    if (result.Ok && root == null)
                    {
                        result.AddProblem($"{arguments.Get("target")} is not an object with widgets.");
                    }

                    var target = Target_Internal(arguments, result);

                    return result.Ok ? FrameworkWidgetActions.AddFrameworkBinders(root, arguments.Flag("dry-run"), null, target) : result;
                }

                case "add-framework-binder":
                {
                    if (!UEArchitectureCli.Require_Internal(arguments, out failed, "target"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var widget = Widget_Internal(arguments, session, result);

                    var player = 1;

                    if (arguments.Get("player") != null && (!int.TryParse(arguments.Get("player"), out player) || player < 1))
                    {
                        result.AddProblem("player= is the player's number, 1 or more (1 is the first player).");
                    }

                    var target = Target_Internal(arguments, result);

                    return result.Ok ? FrameworkWidgetActions.AddFrameworkBinder(widget, player - 1, target) : result;
                }

                case "hud-widgets":
                {
                    if (!UEArchitectureCli.Require_Internal(arguments, out failed, "target"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var hud = Hud_Internal(arguments, session, result);

                    GameMode preview = null;

                    if (!string.IsNullOrEmpty(arguments.Get("preview")))
                    {
                        preview = session.ResolveComponent(arguments.Get("preview"), null, typeof(GameMode), result) as GameMode;

                        if (result.Ok && preview == null)
                        {
                            result.AddProblem($"{arguments.Get("preview")} has no GameMode.");
                        }
                    }

                    return result.Ok ? FrameworkWidgetActions.DescribeHudWidgets(hud, preview, arguments.Flag("strict")) : result;
                }

                case "add-hud-widget":
                {
                    if (!UEArchitectureCli.Require_Internal(arguments, out failed, "target", "view-model"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var hud = Hud_Internal(arguments, session, result);

                    var viewModel = FindClass_Internal(arguments.Get("view-model"), result);

                    Type widgetType = null;

                    if (!string.IsNullOrEmpty(arguments.Get("widget")))
                    {
                        widgetType = FindClass_Internal(arguments.Get("widget"), result);
                    }

                    return result.Ok ? FrameworkWidgetActions.AddHudWidget(hud, viewModel, widgetType, arguments.Get("folder")) : result;
                }

                case "create-widget-for":
                {
                    if (!UEArchitectureCli.Require_Internal(arguments, out failed, "class", "placement"))
                    {
                        return failed;
                    }

                    var result = new ActionResult(arguments.Verb);

                    var type = FindClass_Internal(arguments.Get("class"), result);

                    WidgetPlacement placement;

                    switch (arguments.Get("placement").Trim().ToLowerInvariant())
                    {
                        case "hud":
                            placement = WidgetPlacement.InsideHud;

                            break;

                        case "canvas":
                            placement = WidgetPlacement.Canvas;

                            break;

                        case "prefab":
                            placement = WidgetPlacement.PrefabOnly;

                            break;

                        default:
                            result.AddProblem($"placement=\"{arguments.Get("placement")}\" is not hud, canvas or prefab.");

                            return result;
                    }

                    Object target = null;

                    if (!string.IsNullOrEmpty(arguments.Get("target")))
                    {
                        target = session.Resolve(arguments.Get("target"), result);

                        if (target is GameObject gameObject && placement == WidgetPlacement.InsideHud)
                        {
                            target = gameObject.GetComponent<HUD>();

                            if (target == null && result.Ok)
                            {
                                result.AddProblem($"{arguments.Get("target")} has no HUD.");
                            }
                        }
                    }

                    return result.Ok ? FrameworkWidgetActions.CreateWidgetFor(type, placement, target, arguments.Get("folder")) : result;
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// apply-to=instance (the default: an override on a prefab placed in a level) or apply-to=prefab (the prefab itself).
        /// </summary>
        private static BinderTarget Target_Internal(CliArguments arguments, ActionResult result)
        {
            var value = arguments.Get("apply-to");

            switch (string.IsNullOrWhiteSpace(value) ? "instance" : value.Trim().ToLowerInvariant())
            {
                case "instance":
                    return BinderTarget.Instance;

                case "prefab":
                    return BinderTarget.Prefab;

                default:
                    result.AddProblem($"apply-to=\"{value}\" is not instance or prefab.");

                    return BinderTarget.Instance;
            }
        }

        private static Widget Widget_Internal(CliArguments arguments, CliSession session, ActionResult result)
        {
            var widget = session.ResolveComponent(arguments.Get("target"), arguments.Get("component"), typeof(Widget), result) as Widget;

            if (result.Ok && widget == null)
            {
                result.AddProblem($"{arguments.Get("target")} has no widget.");
            }

            return widget;
        }

        private static HUD Hud_Internal(CliArguments arguments, CliSession session, ActionResult result)
        {
            var hud = session.ResolveComponent(arguments.Get("target"), arguments.Get("component"), typeof(HUD), result) as HUD;

            if (result.Ok && hud == null)
            {
                result.AddProblem($"{arguments.Get("target")} has no HUD.");
            }

            return hud;
        }

        /// <summary>
        /// Any class by its full or plain name (ViewModels are plain classes as often as components).
        /// </summary>
        internal static Type FindClass_Internal(string name, ActionResult result)
        {
            var trimmed = (name ?? string.Empty).Trim();

            var matches = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic).SelectMany(Types_Internal)
                                   .Where(type => type.IsClass && (type.FullName == trimmed || type.Name == trimmed)).Distinct().ToList();

            var exact = matches.Where(type => type.FullName == trimmed).ToList();

            if (exact.Count == 1)
            {
                return exact[0];
            }

            if (matches.Count == 1)
            {
                return matches[0];
            }

            result.AddProblem(matches.Count == 0
                                  ? $"No class is called \"{trimmed}\" (is the script compiled?)."
                                  : $"Several classes are called \"{trimmed}\": {string.Join(", ", matches.Select(type => type.FullName))}. Use the full name.");

            return null;
        }

        private static Type[] Types_Internal(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null).ToArray();
            }
        }
    }
}
