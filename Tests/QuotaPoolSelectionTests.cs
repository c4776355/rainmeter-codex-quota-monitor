using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace CodexQuota
{
    internal static class QuotaPoolSelectionTests
    {
        public static int Main()
        {
            try
            {
                Dictionary<string, object> resetResult = Result(
                    Window(0, 300, 1800000000L),
                    Window(40, 10080, 1900000000L));

                QuotaSnapshot fiveHour = QuotaSnapshot.FromResult(resetResult, "FIVE_HOUR");
                Equal(100, fiveHour.Remaining, "5-hour remaining after reset");
                Equal("5H WINDOW", fiveHour.Window, "5-hour window label");
                Equal(1800000000L, fiveHour.ResetEpoch, "5-hour reset timestamp");
                Equal("FIVE_HOUR", fiveHour.SelectedPool, "5-hour selected pool");
                Equal(false, fiveHour.PoolFallback, "5-hour fallback state");

                QuotaSnapshot weekly = QuotaSnapshot.FromResult(resetResult, "WEEKLY");
                Equal(60, weekly.Remaining, "weekly remaining");
                Equal("1W WINDOW", weekly.Window, "weekly window label");
                Equal(1900000000L, weekly.ResetEpoch, "weekly reset timestamp");
                Equal("WEEKLY", weekly.SelectedPool, "weekly selected pool");

                Dictionary<string, object> swappedResult = Result(
                    Window(40, 10080, 1900000000L),
                    Window(0, 300, 1800000000L));
                QuotaSnapshot swapped = QuotaSnapshot.FromResult(swappedResult, "FIVE_HOUR");
                Equal(100, swapped.Remaining, "duration-based selection with swapped primary/secondary fields");
                Equal("5H WINDOW", swapped.Window, "swapped 5-hour window label");

                Dictionary<string, object> exactWeeklyWithLongerUnknown = Result(
                    Window(15, 20000, 1950000000L),
                    Window(25, 10080, 1900000000L));
                QuotaSnapshot exactWeekly = QuotaSnapshot.FromResult(exactWeeklyWithLongerUnknown, "WEEKLY");
                Equal(75, exactWeekly.Remaining, "exact weekly duration wins over ordering fallback");
                Equal("1W WINDOW", exactWeekly.Window, "exact weekly window remains classified as weekly");

                Dictionary<string, object> exactFiveHourWithShorterUnknown = Result(
                    Window(30, 60, 1750000000L),
                    Window(10, 300, 1800000000L));
                QuotaSnapshot exactFiveHour = QuotaSnapshot.FromResult(exactFiveHourWithShorterUnknown, "FIVE_HOUR");
                Equal(90, exactFiveHour.Remaining, "exact 5-hour duration wins over ordering fallback");
                Equal("5H WINDOW", exactFiveHour.Window, "exact 5-hour window remains classified as 5-hour");

                Dictionary<string, object> weeklyOnlyResult = Result(
                    Window(25, 10080, 1900000000L),
                    null);
                QuotaSnapshot fallback = QuotaSnapshot.FromResult(weeklyOnlyResult, "FIVE_HOUR");
                Equal(75, fallback.Remaining, "single weekly pool remaining");
                Equal("WEEKLY", fallback.SelectedPool, "single-pool fallback selection");
                Equal(true, fallback.PoolFallback, "single-pool fallback marker");

                QuotaSnapshot invalidPreference = QuotaSnapshot.FromResult(resetResult, "AUTO");
                Equal("FIVE_HOUR", invalidPreference.SelectedPool, "unknown preference defaults to 5-hour pool");

                VerifyLegacyWeeklyCacheMigration();

                Console.WriteLine("PASS: quota pool selection remains stable across reset, field order, and single-pool fallback.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.ToString());
                return 1;
            }
        }

        private static Dictionary<string, object> Result(Dictionary<string, object> primary, Dictionary<string, object> secondary)
        {
            Dictionary<string, object> bucket = new Dictionary<string, object>
            {
                { "limitId", "codex" },
                { "planType", "plus" },
                { "primary", primary },
                { "secondary", secondary },
                { "rateLimitReachedType", null }
            };
            return new Dictionary<string, object>
            {
                {
                    "rateLimitsByLimitId",
                    new Dictionary<string, object> { { "codex", bucket } }
                }
            };
        }

        private static Dictionary<string, object> Window(int usedPercent, long durationMinutes, long resetEpoch)
        {
            return new Dictionary<string, object>
            {
                { "usedPercent", usedPercent },
                { "windowDurationMins", durationMinutes },
                { "resetsAt", resetEpoch }
            };
        }

        private static void VerifyLegacyWeeklyCacheMigration()
        {
            string directory = Path.Combine(Path.GetTempPath(), "CodexQuotaPoolMigration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string statePath = Path.Combine(directory, "state.inc");
                File.WriteAllLines(statePath, new[]
                {
                    "Schema=4",
                    "Remaining=61",
                    "Used=39",
                    "Window=1W WINDOW",
                    "Reset=07-22 08:48",
                    "ResetEpoch=1900000000",
                    "PoolMode=FIVE_HOUR"
                });

                DynamicQuotaAgent agent = new DynamicQuotaAgent(
                    statePath,
                    Path.Combine(directory, "stop.token"),
                    Path.Combine(directory, "sync.token"),
                    Path.Combine(directory, "quiet.token"),
                    Path.Combine(directory, "pool.token"),
                    Path.Combine(directory, "sessions"),
                    20,
                    12000,
                    0,
                    null,
                    "FIVE_HOUR");

                FieldInfo stateField = typeof(DynamicQuotaAgent).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
                Dictionary<string, string> state = (Dictionary<string, string>)stateField.GetValue(agent);
                Equal("61", state["WeeklyRemaining"], "legacy weekly remaining migrated into weekly cache");
                Equal("WEEKLY", state["SelectedPool"], "legacy weekly cache remains visibly weekly");
                Equal("1", state["PoolFallback"], "legacy weekly cache marks the default 5-hour request as fallback");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void Equal<T>(T expected, T actual, string name)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    String.Format("{0}: expected {1}, got {2}", name, expected, actual));
            }
        }
    }
}
