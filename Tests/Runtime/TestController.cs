namespace ModernWestern.UEArchitecture.Tests
{
    public sealed class TestController : PlayerController
    {
        public int PawnChanges;

        protected override void OnBeginPlay()
        {
            PawnChanged += static controller => ((TestController)controller).PawnChanges++;
        }
    }
}
