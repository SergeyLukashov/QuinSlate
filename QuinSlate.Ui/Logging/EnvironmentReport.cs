using QuinSlate.Ui.Constants;
using QuinSlate.Ui.Interop;
using QuinSlate.Ui.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace QuinSlate.Ui.Logging;

/// <summary>
/// Collects a snapshot of the host PC configuration for the session banner.
/// Pure and side-effect free so it can be unit tested; every collector fails
/// soft to <c>"unknown"</c> rather than throwing.
/// </summary>
public static class EnvironmentReport
{
    private const string UnknownValue = "unknown";
    private const double BytesPerGiB = 1024.0 * 1024.0 * 1024.0;
    private const double StandardDpi = 96.0;

    /// <summary>
    /// Returns the configuration as ordered key/value pairs. Values are local to
    /// the user's machine (these logs never leave the device).
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Collect()
    {
        var report = new List<KeyValuePair<string, string>>();
        Add(report, "Application", AppConstants.AppName);
        Add(report, "Version", ResolveAppVersion());
        Add(report, "Build", ResolveBuildConfiguration());
        Add(report, "Deployment", AppDataPathResolver.IsPackaged() ? "Packaged (MSIX)" : "Unpackaged");
        Add(report, "OS", RuntimeInformation.OSDescription);
        Add(report, "OS architecture", RuntimeInformation.OSArchitecture.ToString());
        Add(report, "Process architecture", RuntimeInformation.ProcessArchitecture.ToString());
        Add(report, ".NET runtime", RuntimeInformation.FrameworkDescription);
        Add(report, "Logical processors", Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture));
        Add(report, "Physical memory", ResolvePhysicalMemory());
        Add(report, "Displays", ResolveDisplayConfiguration());
        Add(report, "System DPI", ResolveSystemDpi());
        Add(report, "Culture", CultureInfo.CurrentCulture.Name);
        Add(report, "UI culture", CultureInfo.CurrentUICulture.Name);
        Add(report, "Time zone", TimeZoneInfo.Local.DisplayName);
        return report;
    }

    private static void Add(List<KeyValuePair<string, string>> report, string key, string value)
    {
        report.Add(new KeyValuePair<string, string>(key, string.IsNullOrWhiteSpace(value) ? UnknownValue : value));
    }

    private static string ResolveAppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (informational != null && string.IsNullOrWhiteSpace(informational.InformationalVersion) == false)
        {
            return informational.InformationalVersion;
        }

        var version = assembly.GetName().Version;
        if (version == null)
        {
            return UnknownValue;
        }

        return version.ToString();
    }

    private static string ResolveBuildConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private static string ResolvePhysicalMemory()
    {
        var status = new NativeMethods.MEMORYSTATUSEX();
        status.dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>();
        if (NativeMethods.GlobalMemoryStatusEx(ref status) == false)
        {
            return UnknownValue;
        }

        var gib = status.ullTotalPhys / BytesPerGiB;
        return gib.ToString("0.0", CultureInfo.InvariantCulture) + " GiB";
    }

    private static string ResolveDisplayConfiguration()
    {
        var monitors = NativeMethods.GetSystemMetrics(NativeMethods.SM_CMONITORS);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
        return string.Format(CultureInfo.InvariantCulture, "{0} monitor(s), primary {1}x{2}", monitors, width, height);
    }

    private static string ResolveSystemDpi()
    {
        var dpi = NativeMethods.GetDpiForSystem();
        var scalePercent = dpi / StandardDpi * 100.0;
        return string.Format(CultureInfo.InvariantCulture, "{0} ({1:0}%)", dpi, scalePercent);
    }
}
