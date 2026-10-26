using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.EditorTools
{
    public static partial class UEArchitectureActions
    {
        /// <summary>
        /// Fills the empty level references of a component placed in a level (Component and GameObject fields, lists
        /// included) from its own level, in one Undo step: a field gets an object only when exactly one fits (by type, or by
        /// a unique name). Fields that are set are never overwritten; fields with several matches are listed in the notes
        /// for the user to pick. With <paramref name="onlyEmpty"/> false the notes also list the fields that are set.
        /// </summary>
        public static ActionResult FillSceneReferences(Component owner, bool onlyEmpty = true)
        {
            using (var op = Scaffold.Begin($"Fill the level references of {(owner != null ? owner.name : "component")}", "fill-scene-references"))
            {
                FillSceneReferences_Internal(op, owner, onlyEmpty);

                return op.Result;
            }
        }

        internal static List<FillRow> FillSceneReferences_Internal(ScaffoldOp op, Component owner, bool onlyEmpty)
        {
            if (owner == null)
            {
                op.Problem("Pick a component to fill.");

                return new List<FillRow>();
            }

            if (!op.CheckEditable(owner))
            {
                return new List<FillRow>();
            }

            var level = SceneReferenceFill.LevelOf(owner);

            if (!level.IsValid())
            {
                op.Problem($"{ActionResult.Describe(owner)} is in a prefab asset, which cannot point to objects of a level. Fill the copy placed in the level instead.");

                return new List<FillRow>();
            }

            var rows = SceneReferenceFill.Plan(SceneReferenceFill.Slots(new SerializedObject(owner)), SceneReferenceFill.Candidates(level, owner));

            foreach (var row in rows)
            {
                switch (row.Outcome)
                {
                    case FillOutcome.Assign:
                        op.Assign(owner, row.Slot.PropertyPath, row.Pick.Value);

                        op.Result.AddNote("Filled " + row);

                        break;

                    case FillOutcome.Ambiguous:
                        op.Result.AddNote("Several: " + row);

                        break;

                    case FillOutcome.None:
                        op.Result.AddNote("None: " + row);

                        break;

                    default:
                        if (!onlyEmpty)
                        {
                            op.Result.AddNote("Kept: " + row);
                        }

                        break;
                }
            }

            var filled = rows.Count(row => row.Outcome == FillOutcome.Assign);

            var several = rows.Count(row => row.Outcome == FillOutcome.Ambiguous);

            var empty = rows.Count(row => row.Outcome != FillOutcome.Kept);

            op.Result.Result = owner;

            op.Result.Message = empty == 0
                                    ? $"Every level reference of {owner.name} is set."
                                    : $"Filled {filled} of {empty} empty reference(s) of {owner.name}" + (several > 0 ? $"; {several} have several matches: pick them with Find." : ".");

            return rows;
        }
    }
}
