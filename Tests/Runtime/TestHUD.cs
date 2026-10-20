namespace ModernWestern.UEArchitecture.Tests
{
    public sealed class TestHUD : HUD
    {
        public int InitCalls;

        protected override void OnInit() => InitCalls++;
    }
}
