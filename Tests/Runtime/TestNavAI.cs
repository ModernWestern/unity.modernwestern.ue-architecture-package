#if UEA_NAVIGATION
using System;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture.Tests
{
    public sealed class TestNavAI : AIController
    {
        public readonly List<MoveResult> Results = new();

        public Action<TestNavAI, MoveResult> Completed;

        protected override void OnMoveCompleted(MoveResult result)
        {
            Results.Add(result);

            TestGate.Log.Add("AI.MoveCompleted " + result);

            Completed?.Invoke(this, result);
        }

        protected override void OnEndPlay() => TestGate.Log.Add("AI.EndPlay");
    }
}
#endif
