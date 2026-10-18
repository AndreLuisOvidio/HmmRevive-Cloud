using System;
using HeavyMetalMachines;
using HeavyMetalMachines.Combat;
using Pocketverse;
using UnityEngine;

namespace HmmRevive.Legacy
{
    /// <summary>
    /// No own goals while your team carries the ball (1.6, a player's request), like the Steam build: there the goal
    /// (BombTargetTrigger.OnCollisionStay) only blows the ball up when nobody carries it or the carrier's team isn't the
    /// goal's owner. In 2017, BombTargetTrigger.OnTriggerStay calls BombManager.DropDetonator as soon as the ball touches
    /// an ExplodeOnDrop goal, so a carrier standing in their own goal (the goalkeeper spot) scored for the other team.
    /// A ball thrown or dropped into your own goal still counts, as in the Steam build. Server only (the trigger is off on
    /// clients).
    /// </summary>
    public static class OwnGoal
    {
        private static float _nextLog, _nextMove;

        // Start of BombTargetTrigger.OnTriggerStay(other): if (SkipOwnGoal(this, other)) return;
        public static bool Skip(object trigger, object other)
        {
            try
            {
                var goal = trigger as BombTargetTrigger;
                var collider = other as Collider;
                if (goal == null || collider == null || !goal.ExplodeOnDrop || Entry.TestOwnGoal == "off") return false;
                CombatObject combat = CombatRef.GetCombat(collider);
                if (combat == null || !combat.IsBomb) return false;
                BombManager bombs = GameHubBehaviour<HMMHub>.Hub.BombManager;
                if (Entry.TestOwnGoal != null && Time.unscaledTime >= _nextLog)
                    Log.Info($"TEST ball in the {goal.TeamOwner} goal: carried={bombs.IsSomeoneCarryingBomb()} by {bombs.TeamCarryingBomb()} state={bombs.ActiveBomb.State}");
                if (!bombs.IsSomeoneCarryingBomb() || bombs.TeamCarryingBomb() != goal.TeamOwner) return false;
                if (Time.unscaledTime >= _nextLog)
                {
                    _nextLog = Time.unscaledTime + 5f;
                    Log.Info($"own goal prevented: {goal.TeamOwner} carries the ball in its own goal");
                }
                return true;
            }
            catch (Exception e)
            {
                Log.Error("own goal check failed: " + e.Message);
                return false;
            }
        }

        // Test (--hmmrevive-test-own-goal, or =off to run it without the fix): every 4 s the server moves the ball's
        // carrier into its own team's goal, where it then sits for a moment with the ball.
        public static void Tick(HMMHub hub)
        {
            if (Entry.TestOwnGoal == null || Entry.TestOwnGoal == "log" || !Entry.ServerMode || hub.BombManager == null || Time.unscaledTime < _nextMove) return;
            _nextMove = Time.unscaledTime + 4f;
            try
            {
                BombManager bombs = hub.BombManager;
                if (!bombs.IsSomeoneCarryingBomb()) return;
                CombatObject carrier = CombatRef.GetCombat(bombs.ActiveBomb.BombCarriersIds[0]);
                if (carrier == null) return;
                foreach (BombTargetTrigger goal in UnityEngine.Object.FindObjectsOfType(typeof(BombTargetTrigger)))
                {
                    if (!goal.ExplodeOnDrop || goal.TeamOwner != carrier.Team) continue;
                    // The game's own teleport moves the car with what it's linked to (the ball); a plain position change
                    // is undone by the movement's own simulation.
                    Vector3 at = goal.collider != null ? goal.collider.bounds.center : goal.transform.position;
                    CombatMovement move = carrier.GetComponent<CombatMovement>();
                    if (move == null) return;
                    move.ForcePosition(at, true);
                    Log.Info($"TEST moved the carrier ({carrier.Team}) and the ball into its own goal at {at}; score {bombs.ScoreBoard.BombScoreRed}-{bombs.ScoreBoard.BombScoreBlue}");
                    return;
                }
            }
            catch (Exception e) { Log.Info("TEST own goal move failed: " + e.Message); }
        }
    }
}
