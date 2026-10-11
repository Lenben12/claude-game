using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Client → host requests that change the shared game state (Net.CommandHandler).
    /// Client: every Core mutation method calls Net.Command(cmd, args) → one reliable NetCoreMsg.Command frame
    /// [byte cmd][int seq][byte argc][string args…]. Host: CoreCommands.Execute(fromId, cmd, args) runs the same Core
    /// API the host uses itself; afterwards a snapshot is forced and carries "last executed seq" per client, so a client
    /// keeps its optimistic local change until the host's answer arrived (no flicker of money / fish lists).
    public static class NetCommands
    {
        /// Host: the client whose command is executing right now (-1 = none, 0 = the host itself).
        /// NetState uses it to decide who already saw a notification optimistically.
        public static int ScopeFrom { get; private set; } = -1;
        public static NetCommand ScopeCmd { get; private set; }
        /// Client: sequence number of the last command sent.
        public static int LastSentSeq { get; private set; }

        static int nextSeq;
        static bool installed;
        static readonly Dictionary<int, int> lastExecuted = new Dictionary<int, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ScopeFrom = -1; ScopeCmd = NetCommand.Custom; LastSentSeq = 0; nextSeq = 0; installed = false;
            lastExecuted.Clear();
        }

        internal static void Install()
        {
            if (installed) return;
            installed = true;
            NetBus.Register(NetCoreMsg.Command, OnCommand);
        }

        internal static void BeginHost()
        {
            lastExecuted.Clear();
            ScopeFrom = -1;
            Net.CommandHandler = ExecuteLocally;
        }

        internal static void BeginClient()
        {
            nextSeq = 0;
            LastSentSeq = 0;
            Net.CommandHandler = SendToHost;
        }

        internal static void End()
        {
            Net.CommandHandler = null;
            lastExecuted.Clear();
            nextSeq = 0;
            LastSentSeq = 0;
            ScopeFrom = -1;
        }

        internal static void Forget(int playerId) => lastExecuted.Remove(playerId);

        // ------------------------------------------------------------------ client
        static void SendToHost(NetCommand cmd, string[] args)
        {
            if (!Net.IsClient) return;
            int n = args != null ? Mathf.Min(args.Length, 255) : 0;
            int seq = ++nextSeq;
            var w = NetCoreWriter.Begin().Byte((byte)cmd).Int(seq).Byte((byte)n);
            for (int i = 0; i < n; i++) w.Str(args[i]);
            LastSentSeq = seq;
            NetBus.Send(NetCoreMsg.Command, w.ToArray(), NetBus.Everyone, true);
            NetDiagnostics.CommandsSent++;
        }

        // ------------------------------------------------------------------ host
        /// Net.Command called on the host itself (Core never does that, but another module might): run it directly.
        static void ExecuteLocally(NetCommand cmd, string[] args)
        {
            if (!Net.HasAuthority) return;
            Run(Net.LocalId, cmd, args ?? Array.Empty<string>());
        }

        static void OnCommand(int from, byte[] payload)
        {
            if (!Net.IsHost || from == Net.LocalId || !NetSession.IsMember(from)) return;
            var r = new NetCoreReader(payload);
            var cmd = (NetCommand)r.Byte();
            int seq = r.Int();
            int n = r.Byte();
            var args = n == 0 ? Array.Empty<string>() : new string[n];
            for (int i = 0; i < n; i++) args[i] = r.Str();
            Run(from, cmd, args);
            if (!lastExecuted.TryGetValue(from, out int prev) || seq > prev) lastExecuted[from] = seq;
            NetState.ForceSnapshot();
        }

        static void Run(int from, NetCommand cmd, string[] args)
        {
            int prevFrom = ScopeFrom;
            var prevCmd = ScopeCmd;
            ScopeFrom = from;
            ScopeCmd = cmd;
            bool ok = false;
            try
            {
                ok = CoreCommands.Execute(from, cmd, args);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                ScopeFrom = prevFrom;
                ScopeCmd = prevCmd;
            }
            NetDiagnostics.CommandsExecuted++;
            if (!ok) Debug.Log("[Net] command " + cmd + " from player " + from + " was not applied (" + (args.Length > 0 ? args[0] : "") + ")");
        }

        // ------------------------------------------------------------------ acks inside the snapshot header
        internal static void WriteAcks(NetCoreWriter w)
        {
            int n = Mathf.Min(lastExecuted.Count, 255);
            w.Byte((byte)n);
            int i = 0;
            foreach (var kv in lastExecuted)
            {
                if (i++ >= n) break;
                w.Int(kv.Key).Int(kv.Value);
            }
        }

        /// Reads the ack block and returns the last executed command seq of player id (0 when none).
        internal static int ReadAckFor(NetCoreReader r, int id)
        {
            int n = r.Byte();
            int ack = 0;
            for (int i = 0; i < n; i++)
            {
                int k = r.Int();
                int v = r.Int();
                if (k == id) ack = v;
            }
            return ack;
        }
    }
}
