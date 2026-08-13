using System;
using System.Collections.Generic;
using WordCraft.Net;
using WordCraft.Replay; // ReplayLog and ReplayHeader, compiled in from Replay; see Host.csproj
using WordCraft.Sim;

namespace WordCraft.Host
{
    /// <summary>
    /// Keeps what a session actually executed, one entry per tick, and writes it
    /// out in the replay format Replay/ReplayLog.cs already defines.
    ///
    /// It captures LockstepSession.ConfirmedCommands and never the local peer's
    /// own input. Both peers of a match therefore record the same commands in the
    /// same order and write byte identical files; two files that differ are the
    /// desync itself, not an artifact of who did the recording.
    /// </summary>
    internal sealed class MatchRecorder
    {
        private readonly List<Command[]> ticks = new List<Command[]>();

        public int TickCount => ticks.Count;

        /// <summary>
        /// Copies the batch the session just executed. Call it after every
        /// successful TryStep and after nothing else: the log is addressed by
        /// position, so one tick missed or recorded twice slides every command
        /// after it onto the wrong tick, and the file still parses.
        /// </summary>
        public void Capture(LockstepSession session)
        {
            if (session.ConfirmedTick != ticks.Count)
            {
                throw new InvalidOperationException(
                    "the recorder holds " + ticks.Count + " ticks but the session confirmed tick " +
                    session.ConfirmedTick + "; a capture was missed or repeated");
            }

            IReadOnlyList<Command> confirmed = session.ConfirmedCommands;
            var block = new Command[confirmed.Count];
            for (int i = 0; i < block.Length; i++) block[i] = confirmed[i];
            ticks.Add(block);
        }

        /// <summary>
        /// Writes the first tickCount recorded ticks, or everything recorded if
        /// that is fewer.
        ///
        /// The caller names the length because the peers do not agree on it: both
        /// play the same match, but each notices its end on its own schedule and
        /// one can be a tick or two further along than the other when the loop
        /// exits. That is a local decision about when to stop looking, not a
        /// disagreement about what happened, and cutting both files to the length
        /// the match was asked for keeps it from reading like one.
        /// </summary>
        public void Save(string path, ReplayHeader header, int tickCount)
        {
            int count = Math.Min(tickCount, ticks.Count);
            if (count < 0) count = 0;

            var log = new IReadOnlyList<Command>[count];
            for (int t = 0; t < count; t++) log[t] = ticks[t];
            ReplayLog.Write(path, header, log);
        }
    }
}
