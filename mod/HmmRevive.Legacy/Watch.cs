using System;
using System.Reflection;
using HeavyMetalMachines;
using HeavyMetalMachines.Match;
using HeavyMetalMachines.Server;
using Pocketverse;
using UnityEngine;

namespace HmmRevive.Legacy
{
    /// <summary>
    /// A WATCH status line every 5 s (the game's own log is buffered and lost on kill), the match-end timer, and the
    /// server's bots-only start: the server only tells clients to start once every human reported "loaded"
    /// (ServerInfo.OnServerPlayerReady), so a match without humans never starts on its own.
    /// </summary>
    public class Watch : MonoBehaviour
    {
        private float _next, _gameSince = -1f;
        private bool _kicked;
        // Server speed, in the WATCH line: Update / FixedUpdate calls per real second and game seconds per real second
        // (under Wine the server ran slow: the car answered late). Updates are counted in Update, steps in FixedUpdate.
        private int _frames, _steps;
        private float _maxDt, _prevReal, _moved;
        private readonly System.Collections.Generic.Dictionary<int, Vector3> _carPos = new System.Collections.Generic.Dictionary<int, Vector3>();
        private float _lastReal, _lastGame;

        private void FixedUpdate() { _steps++; }

        private void Update()
        {
            HMMHub hub = GameHubBehaviour<HMMHub>.Hub;
            if (hub == null) return;
            _frames++;
            float nowReal = Time.realtimeSinceStartup, gap = _prevReal > 0f ? nowReal - _prevReal : 0f;
            _prevReal = nowReal;
            if (gap > _maxDt) _maxDt = gap;
            if (gap > 0.1f) Log.Info("STALL " + (Entry.ServerMode ? "server" : "client") + " frame took " + (gap * 1000f).ToString("0") + " ms");
            MatchEnd.Tick(hub);
#if Y2017SEP
            if (Entry.ServerMode) TrackCars(hub);
#endif
            Ball.Tick(hub);
            OwnGoal.Tick(hub);
            GameState cur = hub.State != null ? hub.State.Current : null;
            if (cur is ServerGame) { if (_gameSince < 0f) _gameSince = Time.realtimeSinceStartup; }
            else _gameSince = -1f;
            if (Entry.ServerMode) BotsOnlyStart(hub, cur);
            else { Shots.Tick(); Drive.Tick(hub, cur); DropperLook.Tick(hub); }
            if (Time.realtimeSinceStartup < _next) return;
            _next = Time.realtimeSinceStartup + 5f;
            Entry.HideOwnWindows(); // test clients: Unity may show its window again
            try { Log.Info(Line(hub, cur) + Speed()); }
            catch (Exception e) { Log.Info("WATCH failed: " + e.Message); }
        }

        private void BotsOnlyStart(HMMHub hub, GameState cur)
        {
            if (_kicked || !(cur is ServerGame) || hub.MatchMan == null || Time.realtimeSinceStartup - _gameSince < 5f) return;
            if (hub.Match.State != MatchData.MatchState.PreMatch || Humans(hub) > 0) return;
            _kicked = true;
            try
            {
                typeof(ServerInfo).GetMethod("SendServerSetEventToAllClients", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hub.Server, null);
                Log.Info("no players: sent the start signal (ServerSet) myself");
            }
            catch (Exception e) { Log.Error("bots-only start failed: " + e); }
        }

        private static int Humans(HMMHub hub)
        {
#if Y2016
            return hub.Match.Players.Count;
#else
            return hub.Players.Players.Count;
#endif
        }

#if Y2017SEP
        // Metres the cars covered since the last WATCH line (server only): frozen cars at the start of a match show up as 0.
        private void TrackCars(HMMHub hub)
        {
            if (hub.Players == null) return;
            foreach (var p in hub.Players.PlayersAndBots)
            {
                var car = p.CharacterInstance;
                if (car == null) continue;
                Vector3 pos = car.transform.position, last;
                if (_carPos.TryGetValue(p.PlayerCarId, out last)) _moved += Vector3.Distance(pos, last);
                _carPos[p.PlayerCarId] = pos;
            }
        }
#endif

        private string Speed()
        {
            if (!Entry.ServerMode) return "";
            float real = Time.realtimeSinceStartup, dt = real - _lastReal;
            string s = _lastReal > 0f && dt > 0.5f
                ? " | speed fps=" + (_frames / dt).ToString("0.0") + " steps/s=" + (_steps / dt).ToString("0.0") + " maxFrameMs=" + (_maxDt * 1000f).ToString("0") + " carsMoved=" + _moved.ToString("0") + "m game/real=" + ((Time.time - _lastGame) / dt).ToString("0.00")
                : "";
            _lastReal = real; _lastGame = Time.time; _frames = 0; _steps = 0; _maxDt = 0f; _moved = 0f;
            return s;
        }

        private static string Line(HMMHub hub, GameState cur)
        {
            string line = "WATCH state=" + (cur ? cur.name : "-") + " match=" + (hub.Match != null ? hub.Match.State.ToString() : "-");
            if (!Entry.ServerMode)
            {
                var nc = hub.Net as NetworkClient;
                return line + " net=" + (nc != null ? nc.State.ToString() : "-");
            }
            if (hub.BombManager != null && hub.BombManager.ScoreBoard != null)
                line += " score=" + hub.BombManager.ScoreBoard.BombScoreRed + "-" + hub.BombManager.ScoreBoard.BombScoreBlue;
            var pick = cur as PickModeServerSetup;
            if (pick != null)
            {
                var c = hub.Characters;
                line += " stage=" + pick.Stage + " pickTime=" + c.PickTime.ToString("0.0") + " allConfirmed=" + c.AllConfirmed;
#if !Y2016 && !Y2017SEP
                line += " botPicking=" + c.IsBotPicking;
#endif
            }
#if Y2016
            foreach (var p in hub.Match.PlayersAndBots)
                line += " | " + p.Name + (p.IsBot ? "(bot)" : "") + " " + p.Team + " car=" + p.CharacterId + " grid=" + p.GridIndex;
#else
            foreach (var p in hub.Players.PlayersAndBots)
#if Y2017SEP
                line += " | " + p.Name + (p.IsBot ? "(bot)" : "") + " " + p.Team + " car=" + p.CharacterId + " grid=" + p.GridIndex
#else
                line += " | " + p.Name + (p.IsBot ? "(bot)" : "") + " " + p.Team + " car=" + p.PickedCharId + " grid=" + p.GridIndex
#endif
                        + (p.IsBot ? "" : p.Connected ? "" : " (disconnected)");
#endif
            return line;
        }
    }
}
