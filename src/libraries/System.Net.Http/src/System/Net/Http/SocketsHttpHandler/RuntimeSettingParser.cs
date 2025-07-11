// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;

namespace System.Net.Http
{
    internal static class RuntimeSettingParser
    {
        /// <summary>
        /// Parse a <see cref="bool"/> value from an AppContext switch or an environment variable.
        /// </summary>
        public static bool QueryRuntimeSettingSwitch(string appCtxSettingName, string environmentVariableSettingName, bool defaultValue)
        {
            bool value;

            // First check for the AppContext switch, giving it priority over the environment variable.
            // This being first is important for correctness of all callers marked [FeatureSwitchDefinition].
            if (AppContext.TryGetSwitch(appCtxSettingName, out value))
            {
                return value;
            }

            // AppContext switch wasn't used. Check the environment variable.
            string? envVar = Environment.GetEnvironmentVariable(environmentVariableSettingName);

            if (bool.TryParse(envVar, out value))
            {
                return value;
            }
            else if (uint.TryParse(envVar, out uint intVal))
            {
                return intVal != 0;
            }

            return defaultValue;
        }

        /// <summary>
        /// Parse a <see cref="bool"/> value from an AppContext switch.
        /// </summary>
        public static bool QueryRuntimeSettingSwitch(string appCtxSettingName, bool defaultValue)
        {
            bool value;

            // First check for the AppContext switch, giving it priority over the environment variable.
            if (AppContext.TryGetSwitch(appCtxSettingName, out value))
            {
                return value;
            }

            return defaultValue;
        }

        /// <summary>
        /// Parse a <see cref="int"/> value from an AppContext data or an environment variable.
        /// </summary>
        public static int QueryRuntimeSettingInt32(string appCtxSettingName, string environmentVariableSettingName, int defaultValue)
        {
            // First check for the AppContext data, giving it priority over the environment variable.
            switch (AppContext.GetData(appCtxSettingName))
            {
                case uint value:
                    return (int)value;
                case string str:
                    return int.Parse(str, NumberStyles.Any, NumberFormatInfo.InvariantInfo);
                case IConvertible convertible:
                    return convertible.ToInt32(NumberFormatInfo.InvariantInfo);
            }

            // AppContext data wasn't used (or cannot coerce value). Check the environment variable.
            return ParseInt32EnvironmentVariableValue(environmentVariableSettingName, defaultValue);
        }

        /// <summary>
        /// Parse an environment variable for an <see cref="int"/> value.
        /// </summary>
        public static int ParseInt32EnvironmentVariableValue(string environmentVariableSettingName, int defaultValue)
        {
            string? envVar = Environment.GetEnvironmentVariable(environmentVariableSettingName);

            if (int.TryParse(envVar, NumberStyles.Any, CultureInfo.InvariantCulture, out int value))
            {
                return value;
            }
            return defaultValue;
        }

        /// <summary>
        /// Parse an environment variable for a <see cref="double"/> value.
        /// </summary>
        public static double ParseDoubleEnvironmentVariableValue(string environmentVariableSettingName, double defaultValue)
        {
            string? envVar = Environment.GetEnvironmentVariable(environmentVariableSettingName);
            if (double.TryParse(envVar, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                return value;
            }
            return defaultValue;
        }
    }
}
