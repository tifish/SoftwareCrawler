using System.Runtime.InteropServices;

namespace SoftwareCrawler.Services;

/// <summary>
/// Whether now is a bad moment to start a background run. The scheduler asks
/// before every run so an unattended sweep never lands in the middle of a game,
/// a presentation or a call.
/// </summary>
internal static class UserPresence
{
    /// <summary>Values of SHQueryUserNotificationState. See QUERY_USER_NOTIFICATION_STATE.</summary>
    private enum UserNotificationState
    {
        NotPresent = 1,
        Busy = 2,
        RunningD3DFullScreen = 3,
        PresentationMode = 4,
        AcceptsNotifications = 5,
        QuietTime = 6,
        App = 7,
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out UserNotificationState state);

    /// <summary>
    /// True when Windows says the user should not be interrupted. Note that a
    /// locked or logged-out session (NotPresent) is *not* busy — that is the best
    /// time to run, not the worst.
    /// </summary>
    public static bool IsBusy(out string reason)
    {
        reason = "";

        try
        {
            if (SHQueryUserNotificationState(out var state) != 0)
                return false;

            switch (state)
            {
                case UserNotificationState.Busy:
                case UserNotificationState.App:
                    reason = "a full-screen app is running";
                    return true;
                case UserNotificationState.RunningD3DFullScreen:
                    reason = "a full-screen Direct3D app is running";
                    return true;
                case UserNotificationState.PresentationMode:
                    reason = "presentation mode is on";
                    return true;
                case UserNotificationState.QuietTime:
                    reason = "quiet time is on";
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception)
        {
            // Never let a shell query stop a scheduled run.
            return false;
        }
    }
}
