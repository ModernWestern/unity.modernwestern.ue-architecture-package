using System;
using System.IO;
using UnityEngine;
using System.Linq;
using System.Threading.Tasks;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// The data you save, like Unreal's USaveGame: derive from it and add public (or [SerializeField]) fields, the way
    /// Unity serializes them. Save it with <see cref="SaveGames"/>.
    /// <code>
    /// [Serializable] public sealed class Progress : SaveGame { public int level; public float bestTime; }
    ///
    /// SaveGames.Save(progress, "Slot 1");
    /// var progress = SaveGames.Load&lt;Progress&gt;("Slot 1") ?? new Progress();
    /// </code>
    /// </summary>
    [Serializable]
    public abstract class SaveGame
    {
        /// <summary>
        /// The version of your save format. Raise it when the fields change; <see cref="OnAfterLoad"/> receives the
        /// version of the file, so old saves can be upgraded.
        /// </summary>
        public virtual int Version => 1;

        /// <summary>
        /// Called right before it is written: copy runtime state into the fields here.
        /// </summary>
        protected virtual void OnBeforeSave()
        {
        }

        /// <summary>
        /// Called right after it is read, with the version it was saved with.
        /// </summary>
        protected virtual void OnAfterLoad(int savedVersion)
        {
        }

        internal void BeforeSave_Internal()
        {
            OnBeforeSave();
        }

        internal void AfterLoad_Internal(int savedVersion)
        {
            OnAfterLoad(savedVersion);
        }
    }

    /// <summary>
    /// Saves and loads <see cref="SaveGame"/>s in named slots, like Unreal's SaveGameToSlot and LoadGameFromSlot.
    /// Each slot is a JSON file in <see cref="Folder"/>, written safely: a crash while saving leaves the previous save intact.
    /// </summary>
    public static class SaveGames
    {
        [Serializable]
        private sealed class Envelope
        {
            public string type;

            public int version;

            public string savedAtUtc;

            public string data;
        }

        private const string Extension = ".sav";

        private static string _folderOverride;

        /// <summary>
        /// Where the slots are: Application.persistentDataPath/SaveGames.
        /// </summary>
        public static string Folder => _folderOverride ?? Path.Combine(Application.persistentDataPath, "SaveGames");

        /// <summary>
        /// Writes a save game to a slot, replacing what was there. Returns false (and logs why) if it could not be written.
        /// </summary>
        public static bool Save(SaveGame saveGame, string slot)
        {
            if (!Prepare_Internal(saveGame, slot, out var path, out var json))
            {
                return false;
            }

            return Write_Internal(path, json, slot);
        }

        /// <summary>
        /// Like <see cref="Save"/>, writing the file on a background thread. The data is captured right away, so the game
        /// can keep changing it.
        /// </summary>
        public static Task<bool> SaveAsync(SaveGame saveGame, string slot)
        {
            if (!Prepare_Internal(saveGame, slot, out var path, out var json))
            {
                return Task.FromResult(false);
            }

            return Task.Run(() => Write_Internal(path, json, slot));
        }

        /// <summary>
        /// Reads a save game from a slot. Null if the slot is empty, or if the file is damaged or holds another type
        /// (the reason is logged).
        /// </summary>
        public static T Load<T>(string slot) where T : SaveGame
        {
            if (!ValidSlot_Internal(slot, out var path) || !File.Exists(path))
            {
                return null;
            }

            string text;

            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UEArchitecture] Could not read save slot \"{slot}\": {exception.Message}");

                return null;
            }

            return Parse_Internal<T>(text, slot);
        }

        /// <summary>
        /// Like <see cref="Load{T}"/>, reading the file on a background thread.
        /// </summary>
        public static async Task<T> LoadAsync<T>(string slot) where T : SaveGame
        {
            if (!ValidSlot_Internal(slot, out var path) || !File.Exists(path))
            {
                return null;
            }

            string text;

            try
            {
                text = await Task.Run(() => File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UEArchitecture] Could not read save slot \"{slot}\": {exception.Message}");

                return null;
            }

            return Parse_Internal<T>(text, slot);
        }

        public static bool Exists(string slot)
        {
            return ValidSlot_Internal(slot, out var path, false) && File.Exists(path);
        }

        /// <summary>
        /// Deletes a slot. True if there was something to delete.
        /// </summary>
        public static bool Delete(string slot)
        {
            if (!ValidSlot_Internal(slot, out var path) || !File.Exists(path))
            {
                return false;
            }

            File.Delete(path);

            return true;
        }

        /// <summary>
        /// The names of every saved slot.
        /// </summary>
        public static string[] Slots()
        {
            return Directory.Exists(Folder)
                       ? Directory.GetFiles(Folder, "*" + Extension).Select(Path.GetFileNameWithoutExtension).OrderBy(name => name, StringComparer.Ordinal).ToArray()
                       : Array.Empty<string>();
        }

        /// <summary>
        /// Points the slots somewhere else (tests).
        /// </summary>
        internal static void UseFolder_Internal(string folder)
        {
            _folderOverride = folder;
        }

        private static bool Prepare_Internal(SaveGame saveGame, string slot, out string path, out string json)
        {
            json = null;

            if (!ValidSlot_Internal(slot, out path))
            {
                return false;
            }

            if (saveGame == null)
            {
                Debug.LogError("[UEArchitecture] Nothing to save: the save game is null.");

                return false;
            }

            saveGame.BeforeSave_Internal();

            json = JsonUtility.ToJson(new Envelope
            {
                type = saveGame.GetType().FullName,
                version = saveGame.Version,
                savedAtUtc = DateTime.UtcNow.ToString("o"),
                data = JsonUtility.ToJson(saveGame)
            }, true);

            return true;
        }

        // Write next to it, then swap: a crash mid-write never corrupts the existing save.
        private static bool Write_Internal(string path, string json, string slot)
        {
            var temporary = path + ".tmp";

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

                File.WriteAllText(temporary, json);

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(temporary, path, null);
                    }
                    catch (Exception exception) when (exception is PlatformNotSupportedException || exception is IOException)
                    {
                        // Platforms without an atomic replace: the temporary file is complete, so swap it in by hand.
                        File.Delete(path);

                        File.Move(temporary, path);
                    }
                }
                else
                {
                    File.Move(temporary, path);
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[UEArchitecture] Could not save slot \"{slot}\": {exception.Message}");

                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch
                {
                    // Nothing else to do: the previous save is still there.
                }

                return false;
            }
        }

        private static T Parse_Internal<T>(string text, string slot) where T : SaveGame
        {
            Envelope envelope;

            try
            {
                envelope = JsonUtility.FromJson<Envelope>(text);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UEArchitecture] Save slot \"{slot}\" is damaged and was not loaded: {exception.Message}");

                return null;
            }

            if (envelope == null || string.IsNullOrEmpty(envelope.data))
            {
                Debug.LogWarning($"[UEArchitecture] Save slot \"{slot}\" is damaged and was not loaded.");

                return null;
            }

            if (envelope.type != typeof(T).FullName)
            {
                Debug.LogWarning($"[UEArchitecture] Save slot \"{slot}\" holds a {envelope.type}, not a {typeof(T).FullName}.");

                return null;
            }

            T saveGame;

            try
            {
                saveGame = JsonUtility.FromJson<T>(envelope.data);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UEArchitecture] Save slot \"{slot}\" is damaged and was not loaded: {exception.Message}");

                return null;
            }

            if (saveGame == null)
            {
                return null;
            }

            try
            {
                saveGame.AfterLoad_Internal(envelope.version);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            return saveGame;
        }

        private static bool ValidSlot_Internal(string slot, out string path, bool log = true)
        {
            path = null;

            if (string.IsNullOrWhiteSpace(slot) || slot.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                if (log)
                {
                    Debug.LogError($"[UEArchitecture] \"{slot}\" is not a valid save slot name (it becomes a file name).");
                }

                return false;
            }

            path = Path.Combine(Folder, slot + Extension);

            return true;
        }
    }
}
