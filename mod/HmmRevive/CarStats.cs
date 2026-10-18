using System;
using System.Collections.Generic;
using System.Globalization;
using HeavyMetalMachines;
using HeavyMetalMachines.Car;
using HeavyMetalMachines.Combat;
using HeavyMetalMachines.Combat.Gadget;
using HeavyMetalMachines.Match;
using Pocketverse;
using UnityEngine;

namespace HmmRevive
{
    /// <summary>
    /// The lobby's car stats (community PR #3): HP, move, damage and recharge percent per car, or for every car.
    /// --hmmrevive-car-stats=default:100,100,100,100;8:150,120,110,80 (server and clients; car id or name; 10-300, 100 =
    /// the game's own; the recharge number may be left out, as before 1.6). Steam build only.
    /// - HP: max HP gets Info.HPMax x (percent - 100) more, added where CombatData.HPMax adds the level bonus (the game's
    ///   car levels never grow here), on the server and on every client, since each side computes HPMax and only HP is
    ///   streamed. Cars spawn with full HP of the new maximum.
    /// - move: the forward/back acceleration in CarMovement.MovementFixedUpdate (patched where it's stored in _lastAccel),
    ///   which also raises the top speed against the drag curve. Brakes, grip and drifting keep the game's numbers.
    /// - damage: the amount of every HP damage hit the car causes, at the start of CombatController.ApplyInstant,
    ///   before armour and the game's own damage bonuses. Not while the car holds the ball (bomb hits stay as they are).
    /// - recharge (1.6, a player's request): how fast the car fills its ultimate meter (EP). Both ways EP grows are scaled
    ///   for the car that gains it: the passive regen (CombatData.EPRegen, read every tick by CombatData's update) and
    ///   EPRepair modifiers (charge from hits, at the start of CombatController.ApplyInstant, next to the damage). EP
    ///   drains and the meter's size stay. On the server, where EP is decided, and on clients the same way.
    /// Test flags: --hmmrevive-log-damage logs every scaled hit, --hmmrevive-log-charge every EP gain from a modifier and each
    /// car's regen rate (also with the game's own numbers).
    /// </summary>
    public static class CarStats
    {
        public const int MinPct = 10, MaxPct = 300;

        private struct Pct
        {
            public int Hp, Move, Damage, Charge;
            public bool IsVanilla => Hp == 100 && Move == 100 && Damage == 100 && Charge == 100;
            public override string ToString() => $"hp={Hp}% move={Move}% dmg={Damage}% recharge={Charge}%";
        }

        private static readonly Dictionary<string, Pct> ByCar = new Dictionary<string, Pct>(StringComparer.OrdinalIgnoreCase);
        private static readonly Pct Vanilla = new Pct { Hp = 100, Move = 100, Damage = 100, Charge = 100 };
        private static Pct _default = Vanilla;

        private static readonly HashSet<int> Logged = new HashSet<int>(); // car objects whose max HP was logged
        private static readonly Dictionary<int, float> LoggedRegen = new Dictionary<int, float>(); // --hmmrevive-log-charge

        public static bool Active { get; private set; }

        public static void Configure(string compact)
        {
            foreach (string part in (compact ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = part.Split(':');
                string[] n = kv.Length == 2 ? kv[1].Split(',') : new string[0];
                if (n.Length != 3 && n.Length != 4) continue;
                var p = new Pct { Hp = Clamp(n[0]), Move = Clamp(n[1]), Damage = Clamp(n[2]), Charge = n.Length == 4 ? Clamp(n[3]) : 100 };
                string key = kv[0].Trim();
                if (key.Equals("default", StringComparison.OrdinalIgnoreCase)) _default = p;
                else ByCar[key] = p;
            }
            Active = !_default.IsVanilla || ByCar.Count > 0;
            Log.Info($"car stats: every car {_default}" + (ByCar.Count == 0 ? "" : ", " + string.Join(", ", new List<string>(Describe()).ToArray())));
        }

        private static IEnumerable<string> Describe()
        {
            foreach (var kv in ByCar) yield return kv.Key + " " + kv.Value;
        }

        private static int Clamp(string s)
        {
            int v;
            if (!int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) v = 100;
            return Math.Max(MinPct, Math.Min(MaxPct, v));
        }

        private static Pct For(CombatObject combat)
        {
            PlayerData player = combat != null ? combat.Player : null;
            if (player == null) return Vanilla; // not a car
            Pct p;
            if (ByCar.TryGetValue(player.CharacterId.ToString(CultureInfo.InvariantCulture), out p)) return p;
            if (player.Character != null && ByCar.TryGetValue(player.Character.name, out p)) return p;
            return _default;
        }

        // CombatData.HPMax (server and clients): "+ _levelHPMax" becomes "+ LevelHpMax(_levelHPMax, this)".
        public static float LevelHpMax(float level, object combatData)
        {
            if (!Active) return level;
            var data = combatData as CombatData;
            if (data == null || data.Info == null) return level;
            int pct = For(data.Combat).Hp;
            if (pct == 100) return level;
            float bonus = data.Info.HPMax * (pct - 100) / 100f;
            if (data.Combat != null && data.Combat.Id != null && Logged.Add(data.Combat.Id.ObjId))
                Log.Info($"car stats: {data.Combat.Player?.Name} (car {data.Combat.Player?.CharacterId}) max HP {data.Info.HPMax} {(bonus > 0 ? "+" : "")}{bonus}");
            return level + bonus;
        }

        // CarMovement.MovementFixedUpdate, where the forward/back acceleration is stored in _lastAccel.
        public static float ScaleAcceleration(float accel, object movement)
        {
            if (!Active) return accel;
            var m = movement as CarMovement;
            int pct = m != null ? For(m.Combat).Move : 100;
            return pct == 100 ? accel : accel * pct / 100f;
        }

        // Start of CombatController.ApplyInstant(mod, causer, eventId): amount = ScaleDamage(mod.Amount, mod, causer).
        public static float ScaleDamage(float amount, object modifier, object causer)
        {
            if (!Active) return amount;
            var mod = modifier as ModifierInstance;
            var car = causer as CombatObject;
            if (mod == null || car == null || car.Player == null || !IsHpDamage(mod.Info.Effect)) return amount;
            int pct = For(car).Damage;
            if (pct == 100 || HoldsBall(car)) return amount;
            if (Entry.LogDamage) Log.Info($"DAMAGE {car.Player.Name} {mod.Info.Effect} {amount:0.##} x{pct}% = {amount * pct / 100f:0.##}");
            return amount * pct / 100f;
        }

        // Start of CombatController.ApplyInstant, after ScaleDamage: amount = ScaleCharge(amount, mod, this). The car is the
        // one the modifier lands on.
        public static float ScaleCharge(float amount, object modifier, object controller)
        {
            if (!Active && !Entry.LogCharge) return amount;
            var mod = modifier as ModifierInstance;
            var cc = controller as CombatController;
            if (mod == null || mod.Info.Effect != EffectKind.EPRepair || cc == null || cc.Combat == null || cc.Combat.Player == null) return amount;
            int pct = For(cc.Combat).Charge;
            float scaled = pct == 100 ? amount : amount * pct / 100f;
            if (Entry.LogCharge)
                Log.Info($"CHARGE {cc.Combat.Player.Name} (car {cc.Combat.Player.CharacterId}) EPRepair {amount:0.###}{(mod.Info.IsPercent ? " x max" : "")} x{pct}% = {scaled:0.###}"
                    + $" from {(mod.Gadget != null ? mod.Gadget.name : "?")}, EP {cc.Combat.Data.EP:0}/{cc.Combat.Data.EPMax}");
            return scaled;
        }

        // CombatData.EPRegen (EP per second, server and clients): before it returns, regen = ScaleEpRegen(regen, this).
        public static float ScaleEpRegen(float regen, object combatData)
        {
            if (!Active && !Entry.LogCharge) return regen;
            var data = combatData as CombatData;
            if (data == null || data.Combat == null || data.Combat.Player == null) return regen;
            int pct = For(data.Combat).Charge;
            float scaled = pct == 100 ? regen : regen * pct / 100f;
            if (Entry.LogCharge && data.Combat.Id != null)
            {
                float last;
                if (!LoggedRegen.TryGetValue(data.Combat.Id.ObjId, out last) || Math.Abs(last - scaled) > 0.001f)
                {
                    LoggedRegen[data.Combat.Id.ObjId] = scaled;
                    Log.Info($"CHARGE {data.Combat.Player.Name} (car {data.Combat.Player.CharacterId}) regen {regen:0.###}/s x{pct}% = {scaled:0.###}/s, EP max {data.EPMax}");
                }
            }
            return scaled;
        }

        private static bool IsHpDamage(EffectKind e) =>
            e == EffectKind.HPPureDamage || e == EffectKind.HPPureDamageNL || e == EffectKind.HPLightDamage || e == EffectKind.HPHeavyDamage;

        private static bool HoldsBall(CombatObject car)
        {
            if (car.IsCarryingBomb) return true;
            var bomb = car.BombGadget as BombGadget;
            return bomb != null && bomb.CurrentState != BombGadget.State.Disabled;
        }
    }
}
