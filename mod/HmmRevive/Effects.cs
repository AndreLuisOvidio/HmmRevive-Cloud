using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assets.ClientApiObjects;
using Assets.ClientApiObjects.Components;
using HeavyMetalMachines;
using HeavyMetalMachines.DataTransferObjects.Battlepass;
using HeavyMetalMachines.Localization;
using HeavyMetalMachines.Match;
using Hoplon.Unity.Loading;
using Pocketverse;
using UnityEngine;

namespace HmmRevive
{
    /// <summary>
    /// Cosmetic effects picked like skins (1.6): takeoff (leaving the base), respawn, takedown (destroying a car) and
    /// scoring. The game plays them through CustomizationHubMasterVFX, which reads the effect item from the owner's
    /// customization slot (TakeOffVFX, RespawnVFX, KillVFX, ScoreVFX) and falls back to CourtesyItemCollection's default
    /// when the slot is empty, as it always was offline. LoadingState's CustomizationPreLoader preloads every player's
    /// effect prefabs from the same slots. So, like the emotes: the client sends its four picks with the login
    /// (--hmmrevive-effects=T,R,K,S, numbers in <see cref="List"/> per kind, -1 = the game's default), the server fills
    /// the player's slots, and the player data carries them to every client.
    /// </summary>
    public static class Effects
    {
        public static readonly PlayerCustomizationSlot[] Slots =
            { PlayerCustomizationSlot.TakeOffVFX, PlayerCustomizationSlot.RespawnVFX, PlayerCustomizationSlot.KillVFX, PlayerCustomizationSlot.ScoreVFX };
        public static readonly string[] Kinds = { "takeoff", "respawn", "takedown", "score" }; // as in hmmrevive-effects.txt

        private static readonly Dictionary<string, string> WishByName = new Dictionary<string, string>();
        private static List<ItemTypeScriptableObject>[] _lists;

        /// <summary>
        /// Per kind (index as in <see cref="Slots"/>), the effects whose prefabs all ship with the game, in a stable order (by
        /// item name). Numbers are positions. Items of a category made for that slot, with a VfxItemTypeComponent.
        /// </summary>
        public static List<ItemTypeScriptableObject>[] List()
        {
            if (_lists != null) return _lists;
            var collection = GameHubBehaviour.Hub?.InventoryColletion;
            if (collection == null || collection.AllItemTypes.Count == 0) return Slots.Select(s => new List<ItemTypeScriptableObject>()).ToArray();
            var shipped = new HashSet<string>(Loading.Content.content.Select(c => c.AssetName.ToLowerInvariant()));
            _lists = Slots.Select(slot => collection.AllItemTypes.Values.Where(i =>
            {
                try
                {
                    ItemCategoryScriptableObject cat = collection.GetCategoryById(i.ItemCategoryId);
                    if (cat == null || cat.CustomizationSlots == null || !cat.CustomizationSlots.Contains(slot)) return false;
                    VfxItemTypeComponent vfx = i.GetComponent<VfxItemTypeComponent>();
                    if (vfx == null || vfx.ArraySize == 0) return false;
                    for (int n = 0; n < vfx.ArraySize; n++)
                    {
                        string prefab = vfx.GetPrefabNameAt(n);
                        if (string.IsNullOrEmpty(prefab) || !shipped.Contains(prefab.ToLowerInvariant())) return false;
                    }
                    return true;
                }
                catch { return false; }
            }).OrderBy(i => i.Name, StringComparer.Ordinal).ToList()).ToArray();
            for (int k = 0; k < Slots.Length; k++)
                Log.Info($"effects {Kinds[k]}: {_lists[k].Count} shipped: " + string.Join(", ", _lists[k].Select((e, n) => n + "=" + e.Name).ToArray()));
            Write(_lists);
            return _lists;
        }

        // hmmrevive-effects.txt next to HMM.exe ("kind<TAB>number<TAB>item name<TAB>title"), like hmmrevive-emotes.txt.
        private static void Write(List<ItemTypeScriptableObject>[] lists)
        {
            try
            {
                var lines = new List<string>();
                for (int k = 0; k < Slots.Length; k++)
                    lines.AddRange(lists[k].Select((e, n) => Kinds[k] + "\t" + n + "\t" + e.Name + "\t" + Title(e)));
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "hmmrevive-effects.txt"), string.Join("\r\n", lines.ToArray()) + "\r\n");
            }
            catch (Exception e) { Log.Error("effect list failed: " + e.Message); }
        }

        /// <summary>"3,-1,0,12" → the four effects (Guid.Empty = the game's default, also for wrong numbers).</summary>
        public static Guid[] Resolve(string wish)
        {
            List<ItemTypeScriptableObject>[] lists = List();
            string[] parts = (wish ?? "").Split(new[] { ',', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var picked = new Guid[Slots.Length];
            for (int k = 0; k < Slots.Length; k++)
            {
                int n;
                picked[k] = k < parts.Length && int.TryParse(parts[k], out n) && n >= 0 && n < lists[k].Count ? lists[k][n].Id : Guid.Empty;
            }
            return picked;
        }

        // Client: the login field ("name#car#skin#team#emotes#effects"), dots so it survives the login's commas.
        public static string LoginField => Entry.Effects == null ? null : Entry.Effects.Replace(',', '.');

        // Server: from the login (CarChoice.TakeFromLogin).
        public static void Wish(string name, string effects) { WishByName[name] = effects; }

        // Server, during character selection (CarChoice.CharacterIndex), next to the emotes. Bots keep the game's defaults,
        // except with the test flag --hmmrevive-test-bot-effects=T,R,K,S (bots die and score; a hidden test player doesn't).
        public static void ApplyToPlayer(PlayerData player)
        {
            if (player == null || player.IsNarrator) return;
            string wish = null;
            if (player.IsBot ? (wish = Entry.TestBotEffects) == null : !WishByName.TryGetValue(player.Name, out wish)) return; // the game's defaults
            try
            {
                Guid[] effects = Resolve(wish);
                for (int k = 0; k < Slots.Length; k++) player.Customizations.SetGuidAndSlot(Slots[k], effects[k]);
                Log.Info($"player {player.Name} effects {wish} = " + string.Join(", ", effects.Select((g, k) => Kinds[k] + " " + Name(g)).ToArray()));
            }
            catch (Exception e) { Log.Error("effects for " + player.Name + " failed: " + e); }
        }

        // The name the game's inventory showed (in the game's language), else the item name.
        private static string Title(ItemTypeScriptableObject item)
        {
            string name = null;
            try { name = Language.Get(item.GetComponent<InventoryItemTypeComponent>().TitleDraft, TranslationContext.Items); }
            catch { }
            return string.IsNullOrEmpty(name) || name.StartsWith("#") ? item.Name : name.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        // Start of CustomizationHubMasterVFX.Activate(owner, target, effect), test flag --hmmrevive-log-effects: which effect
        // item a customization effect plays, on every client.
        public static void Activated(object vfx, object owner, object target)
        {
            if (!Entry.LogEffects) return;
            try
            {
                var t = vfx.GetType();
                var slot = (PlayerCustomizationSlot)t.GetField("_itemSlot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(vfx);
                object who = t.GetField("_actualOwner", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(vfx).ToString() == "Owner" ? owner : target;
                var id = who as Identifiable;
                PlayerData p = id != null ? GameHubBehaviour.Hub.Players.GetPlayerOrBotsByObjectId(id.ObjId) : null;
                ItemTypeScriptableObject item = p != null ? GameHubBehaviour.Hub.CustomizationAssets.GetItemTypeScriptableObjectBySlot(slot, p.Customizations) : null;
                Log.Info($"EFFECT {slot} of {(p != null ? p.Name : "?")}: {(item != null ? item.Name : "?")}");
            }
            catch (Exception e) { Log.Info("EFFECT log failed: " + e.Message); }
        }

        private static string Name(Guid id)
        {
            ItemTypeScriptableObject item;
            return id != Guid.Empty && GameHubBehaviour.Hub.InventoryColletion.AllItemTypes.TryGetValue(id, out item) ? item.Name : "default";
        }
    }
}
