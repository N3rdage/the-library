using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Microsoft.Identity.Client;

namespace BookTracker.Mobile;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Honour the system reduced-motion setting: animator-duration-scale of 0
        // (Developer options / accessibility) disables the §9 entrance motion.
        var scale = Settings.Global.GetFloat(ContentResolver, Settings.Global.AnimatorDurationScale, 1f);
        Theming.Motion.Enabled = scale > 0f;
    }

    // MSAL.NET's interactive sign-in launches a Chrome Custom Tab and
    // returns the auth code via Activity.onActivityResult. Forwarding
    // it through this hook lets MSAL's continuation logic complete
    // the token exchange. Required for the Android flow per
    // aka.ms/msal-net-xamarin-android-considerations.
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        AuthenticationContinuationHelper.SetAuthenticationContinuationEventArgs(
            requestCode, resultCode, data);
    }
}
