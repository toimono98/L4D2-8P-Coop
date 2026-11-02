using System.Reflection;

namespace L4D2EightPlayer.Core
{
    /// <summary>The running app's version (set once in Directory.Build.props for both apps).</summary>
    public static class AppInfo
    {
        public static readonly string Version = ReadVersion();

        static string ReadVersion()
        {
            var v = typeof(AppInfo).Assembly.GetName().Version;
            return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }
}
