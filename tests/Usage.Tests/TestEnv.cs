using System.Runtime.CompilerServices;

static class TestEnv
{
    // keep the tests away from the real app data (and its diagnostic log)
    [ModuleInitializer]
    internal static void Init() => Environment.SetEnvironmentVariable("USAGE_DATA_DIR", Path.Combine(Path.GetTempPath(), "usage-tests-data"));
}
