using QuinSlate.Ui.Constants;
using QuinSlate.Ui.Interop;
using System;
using System.IO;

namespace QuinSlate.Ui.Services;

/// <summary>
/// Resolves the single directory the application reads and writes its data in.
/// The location differs by deployment model so it is always the real on-disk
/// folder in both cases:
/// <list type="bullet">
/// <item>Packaged (installed via MSIX): the package's own per-user
/// <see cref="Windows.Storage.ApplicationData.LocalFolder"/>. Writing to
/// <c>%AppData%\Roaming</c> from inside the package would be silently virtualised,
/// so the unredirected path would not match where the data actually lands.</item>
/// <item>Unpackaged (e.g. launched from Visual Studio): <c>%AppData%\Roaming\QuinSlate\</c>.
/// The Windows.Storage <c>ApplicationData.Current</c> API throws without package
/// identity, so it cannot be used here.</item>
/// </list>
/// </summary>
public static class AppDataPathResolver
{
    private const string AppDataFolderName = AppConstants.AppName;

    /// <summary>
    /// Returns the absolute data directory for the current deployment model.
    /// The directory is not guaranteed to exist; callers create it as needed.
    /// </summary>
    public static string Resolve()
    {
        if (HasPackageIdentity())
        {
            return Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }

        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(roaming, AppDataFolderName);
    }

    /// <summary>
    /// Detects whether the process is running with package identity (i.e. it was
    /// installed as an MSIX package) by asking Win32 for the current package name.
    /// </summary>
    private static bool HasPackageIdentity()
    {
        var length = 0;
        var result = NativeMethods.GetCurrentPackageFullName(ref length, null);
        return result != NativeMethods.APPMODEL_ERROR_NO_PACKAGE;
    }
}
