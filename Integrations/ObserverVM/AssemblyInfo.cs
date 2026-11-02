using UnityEngine.Scripting;

// Nothing in a game references this assembly's types: it only hooks in through [RuntimeInitializeOnLoadMethod]. Without this
// attribute the IL2CPP linker removes the whole assembly, and no HUD widget gets its ViewModel silently stops working.
[assembly: AlwaysLinkAssembly]
