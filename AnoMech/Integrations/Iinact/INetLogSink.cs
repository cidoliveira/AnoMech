namespace AnoMech.Integrations.Iinact;

// Where a sim stay's log lines go: IINACT's log writer in game, a recorder in tests.
internal interface INetLogSink
{
    void Write(NetLogEntry entry);

    // Keeps whatever goes through the sink off the log file on disk.
    bool KeepOffDisk { get; set; }
}
