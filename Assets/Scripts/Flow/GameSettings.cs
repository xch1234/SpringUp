using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpringUp.Flow
{
    public enum GameBinding { Up, Down, Left, Right, Sprint, Interact, Inventory }

    // Local preferences only: never reads or writes a run's progress.
    public static class GameSettings
    {
        const string PreferenceKey = "SpringUp.Settings.v1";
        static readonly Key[] Defaults = { Key.W, Key.S, Key.A, Key.D, Key.LeftShift, Key.E, Key.B };
        public static readonly string[] Labels = { "向上移动", "向下移动", "向左移动", "向右移动", "疾跑", "交互", "背包" };
        [Serializable] sealed class Data { public float music = .55f, sfx = .85f; public Key[] keys = (Key[])Defaults.Clone(); }
        static Data data;
        public static event Action Changed;
        static Data Current { get { if (data == null) Load(); return data; } }
        public static float Music => Current.music;
        public static float Sfx => Current.sfx;
        public static Key Binding(GameBinding action) => Current.keys[(int)action];
        public static string Display(GameBinding action) => Keyboard.current?[Binding(action)]?.displayName ?? Binding(action).ToString();
        public static bool Pressed(GameBinding action) => Keyboard.current?[Binding(action)]?.wasPressedThisFrame == true;
        public static string InventoryHint => Display(GameBinding.Inventory) + " / Tab";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic() { data = null; Changed = null; }
        public static bool IsReserved(Key key) => key == Key.Escape || key == Key.Tab || key == Key.UpArrow
            || key == Key.DownArrow || key == Key.LeftArrow || key == Key.RightArrow;
        static bool Valid(Key key) => key != Key.None && Enum.IsDefined(typeof(Key), key) && !IsReserved(key);
        public static void Load()
        {
            try { data = JsonUtility.FromJson<Data>(PlayerPrefs.GetString(PreferenceKey, "")); }
            catch (ArgumentException) { data = null; }
            data ??= new Data();
            data.music = float.IsNaN(data.music) ? .55f : Mathf.Clamp01(data.music);
            data.sfx = float.IsNaN(data.sfx) ? .85f : Mathf.Clamp01(data.sfx);
            bool valid = data.keys != null && data.keys.Length == Defaults.Length;
            if (valid)
                for (int i = 0; i < data.keys.Length; i++)
                {
                    if (!Valid(data.keys[i])) valid = false;
                    for (int j = 0; j < i; j++) if (data.keys[i] == data.keys[j]) valid = false;
                }
            if (!valid) data.keys = (Key[])Defaults.Clone();
        }
        static void Save()
        {
            PlayerPrefs.SetString(PreferenceKey, JsonUtility.ToJson(Current));
            PlayerPrefs.Save(); Changed?.Invoke();
        }
        public static void SetVolumes(float music, float sfx)
        { Current.music = Mathf.Clamp01(music); Current.sfx = Mathf.Clamp01(sfx); Save(); }
        public static bool TryBind(GameBinding action, Key key, out string error)
        {
            if (!Valid(key)) { error = "该键为保留键，请选择其他按键。"; return false; }
            for (int i = 0; i < Current.keys.Length; i++)
                if (i != (int)action && Current.keys[i] == key)
                { error = "该键已用于“" + Labels[i] + "”，原绑定未改变。"; return false; }
            Current.keys[(int)action] = key; Save(); error = null; return true;
        }
        public static void RestoreDefaults() { data = new Data(); Save(); }
        public static void ApplyMovement(InputAction move, InputAction sprint)
        {
            string[] names = { "up", "down", "left", "right" };
            if (move != null)
                for (int i = 0; i < move.bindings.Count; i++)
                {
                    var binding = move.bindings[i];
                    if (!binding.isPartOfComposite || !binding.path.StartsWith("<Keyboard>/") || binding.path.EndsWith("Arrow")) continue;
                    int index = Array.IndexOf(names, binding.name);
                    if (index >= 0) move.ApplyBindingOverride(i, Path((GameBinding)index));
                }
            if (sprint != null)
                for (int i = 0; i < sprint.bindings.Count; i++)
                    if (sprint.bindings[i].path.StartsWith("<Keyboard>/")) sprint.ApplyBindingOverride(i, Path(GameBinding.Sprint));
        }
        static string Path(GameBinding action) => "<Keyboard>/" + (Keyboard.current?[Binding(action)]?.name ?? Binding(action).ToString().ToLowerInvariant());
    }
}
