namespace UEArchitecture
{
    public interface IDataPersistent<TPlayerState, TGameEvents, TGameState> where TPlayerState : IPersistent
                                                                            where TGameEvents  : IPersistent
                                                                            where TGameState   : IPersistent
    {
        TPlayerState PlayerState { get; set; }
        TGameEvents GameEvents { get; set; }
        TGameState GameState { get; set; }
    }


    public interface IPersistent
    {
    }
}