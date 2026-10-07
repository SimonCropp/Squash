public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        // BuildEventArgs carries a lot that says nothing about the diagnostic: a timestamp, the
        // thread it was raised on, the sender. Stripping them leaves a snapshot of just the code and
        // the message, which is the part worth reviewing in a diff.
        VerifierSettings.IgnoreMembers(
            "HelpKeyword",
            "SenderName",
            "ContinueOnError",
            "ProjectFileOfTaskNode",
            "File",
            "Subcategory",
            "Timestamp",
            "BuildEventContext",
            "Importance");
        VerifierSettings.IgnoreMember<BuildEventArgs>(_ => _.ThreadId);
        VerifierSettings.Inline(maxLines: 10, applyMaxLinesToExisting: true);

        // Most tests block a thread while a linker process runs, and the task reads that process's
        // output on further pool threads. Left to grow at its own pace the pool adds one thread a
        // second, and the suite spends most of its time waiting for them.
        System.Threading.ThreadPool.SetMinThreads(64, 64);
    }
}
