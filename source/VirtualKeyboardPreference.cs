using System;

namespace CoopFeasibilityMod;

public interface IBooleanPreferenceStore
{
    bool GetBool(string key, bool defaultValue);
    void SetBool(string key, bool value);
    void Save();
}

// Small pure adapter keeps the safe missing-key default and explicit persistence behavior
// independently testable without coupling tests to the runtime settings UI.
public static class VirtualKeyboardPreference
{
    public const string Key = "mods.coop-feasibility.virtualPlayer2Keyboard";
    public static bool Load(IBooleanPreferenceStore store) => store.GetBool(Key, true);
    public static void Persist(IBooleanPreferenceStore store, bool enabled)
    {
        store.SetBool(Key, enabled);
        store.Save();
    }
}
