using System;
using System.Collections.Generic;
using System.Globalization;

namespace Disappearance.Comfort
{
    internal static class FovMath
    {
        internal static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        internal static double Horizontal(double vertical, double aspect) => 360 / Math.PI * Math.Atan(Math.Tan(vertical * Math.PI / 360) * aspect);
        internal static double Vertical(double horizontal, double aspect) => 360 / Math.PI * Math.Atan(Math.Tan(horizontal * Math.PI / 360) / aspect);
        internal static double DefaultHorizontal(double vertical, double aspect) => Math.Round(Horizontal(vertical, aspect), MidpointRounding.AwayFromZero);
        internal static double Clamp(double x, double min, double max) => Math.Max(min, Math.Min(max, x));
    }

    internal static class FovSpotlightFit
    {
        internal static (float Outer, float Inner) Angles(float outer, float inner, float horizontal, float baseline, bool enabled)
        {
            float extra = enabled ? Math.Max(0, horizontal - baseline) : 0;
            if (extra == 0) return (outer, inner);
            float fitted = Math.Min(outer + extra, 175);
            return (fitted, Math.Min(inner + extra, fitted - 1));
        }
    }

    internal sealed class SettingSpec
    {
        internal string Id, MigrationId, Section, Key, Label, Format;
        internal double Min, Max, Step, Default;
        internal int Order, Decimals;
        internal bool Trim;
        internal string Path => Section + "." + Key;
        internal bool Accept(double value) => FovMath.Finite(value) && value >= Min && value <= Max;
        internal static readonly SettingSpec[] All = {
            new SettingSpec { Id="mouse_sensitivity", MigrationId="S01", Section="Look", Key="MouseLevel", Label="マウス感度", Min=1, Max=10, Step=.25, Default=5, Order=10, Decimals=2, Format="0.00" },
            new SettingSpec { Id="controller_sensitivity", MigrationId="S02", Section="Look", Key="ControllerLevel", Label="コントローラー感度", Min=1, Max=10, Step=.25, Default=5, Order=20, Decimals=2, Format="0.00" },
            new SettingSpec { Id="horizontal_fov", MigrationId="S04", Section="Camera", Key="HorizontalFov", Label="水平視野角", Min=20, Max=120, Step=1, Default=0, Order=30, Decimals=0, Format="0" },
            new SettingSpec { Id="player_brightness", MigrationId="S03", Section="Display", Key="BrightnessLevel", Label="明るさ", Min=0, Max=10, Step=1, Default=5, Order=40, Decimals=2, Trim=true, Format="0.##" }
        };
        internal static SettingSpec Find(string id) => Array.Find(All, x => x.Id == id);
        internal static string Number(double x) => x.ToString("R", CultureInfo.InvariantCulture);
    }

    // Also used by the rotation Prefix/Postfix/Finalizer: restoration is consumed before invoking it.
    internal sealed class RestoreOnce
    {
        private Action restore;
        internal RestoreOnce(Action restore) { this.restore = restore; }
        internal void Restore() { Action action = restore; restore = null; action?.Invoke(); }
    }

    internal static class WriteTransaction
    {
        internal static bool Run(Action apply, Action save, Action rollback, Action<Exception> report)
        {
            try { apply(); save(); return true; }
            catch (Exception error)
            {
                try { rollback(); } catch (Exception restoreError) { report(restoreError); }
                report(error);
                return false;
            }
        }
    }
}
