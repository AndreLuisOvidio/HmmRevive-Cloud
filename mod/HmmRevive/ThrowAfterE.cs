using System;
using System.Collections.Generic;
using HeavyMetalMachines.Combat;
using UnityEngine;

namespace HmmRevive
{
    /// <summary>
    /// Investigation for "every car can throw the ball after its E" (a player's request: Killer J., Metal Herald and Black
    /// Lotus can't). A car refuses to throw while its status has PassBlocked (CombatAttributes.IsGadgetDisarmed for the
    /// bomb slot), and statuses come from the modifiers on the car (CombatAttributes.Apply: _currentStatus |=
    /// mod.Data.Status). Found (bot matches, 1.6): nearly every car blocks its own throws while its weapons fire (Little
    /// Monster's Weapon01/02 too), so dropping self-applied PassBlocked would let everyone throw mid-attack. Which ability
    /// is "E" and which block lingers is still open, so this only logs for now.
    /// Test flag --hmmrevive-log-pass-blocked (=all: every application) logs who gets PassBlocked, from whom and which gadget.
    /// </summary>
    public static class ThrowAfterE
    {
        private static readonly Dictionary<string, float> NextLog = new Dictionary<string, float>();

        // CombatAttributes.Apply(mod, ...), at "_currentStatus |= mod.Data.Status": status = FilterStatus(status, mod, this).
        public static StatusKind FilterStatus(StatusKind status, object modifier, object attributes)
        {
            if ((status & StatusKind.PassBlocked) == 0) return status;
            try
            {
                var mod = modifier as ModifierInstance;
                var attrs = attributes as CombatAttributes;
                CombatObject car = attrs != null ? attrs.Combat : null;
                bool self = mod != null && car != null && mod.Causer == car;
                if (Entry.LogPassBlocked && car != null && car.Player != null)
                {
                    string key = car.Player.Name + (mod != null && mod.Gadget != null ? mod.Gadget.name : "?");
                    float next;
                    if (!NextLog.TryGetValue(key, out next) || Time.unscaledTime >= next || Entry.LogPassBlockedAll)
                    {
                        NextLog[key] = Time.unscaledTime + 3f;
                        Log.Info($"PASSBLOCKED {car.Player.Name} ({car.Player.Character?.name}) from {(mod?.Causer?.Player != null ? mod.Causer.Player.Name : "?")}"
                            + $" gadget {(mod?.Gadget != null ? mod.Gadget.name : "?")} status {status}{(self ? ": own" : "")}");
                    }
                }
                return status; // unchanged until it is known which block lingers after E
            }
            catch (Exception e)
            {
                Log.Error("pass blocked filter failed: " + e.Message);
                return status;
            }
        }
    }
}
