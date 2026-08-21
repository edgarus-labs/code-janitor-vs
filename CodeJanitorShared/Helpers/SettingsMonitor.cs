using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace CodeJanitor.Helpers;

public sealed class SettingsMonitor<TSetting>
    where TSetting : ApplicationSettingsBase
{
    private readonly JoinableTaskFactory _joinableTaskFactory;
    private readonly Dictionary<string[], Monitor> _monitors = new Dictionary<string[], Monitor>(new StringArrayComparer());
    private readonly TSetting _settings;

    public SettingsMonitor(TSetting settings, JoinableTaskFactory joinableTaskFactory)
    {
        _joinableTaskFactory = joinableTaskFactory;
        _settings = settings;
        _settings.SettingsSaving += OnSettingsSaving;
    }

    /// <summary>
    /// Extracts the member name from the setting expression and registers a watch via the overloaded WatchAsync, invoking the provided callback with the single changed value when it fires.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <param name="changedCallback">The changed callback.</param>
    /// <returns>A Task value produced by this method.</returns>

    public async Task WatchAsync<TValue>(Expression<Func<TSetting, TValue>> setting, Func<TValue, Task> changedCallback)
    {
        var settingName = (setting.Body as MemberExpression).Member.Name;
        await WatchAsync<TValue>(new[] { settingName }, async values => await changedCallback(values[0]));
    }

    /// <summary>
    /// Wraps a strongly typed TValue[] callback by casting each object value to TValue and awaiting the underlying WatchAsync overload, which asynchronously invokes the provided callback whenever the watched settings change.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <param name="changedCallback">The changed callback.</param>
    /// <returns>A Task value produced by this method.</returns>

    public async Task WatchAsync<TValue>(string[] settings, Func<TValue[], Task> changedCallback)
    {
        await WatchAsync(settings, async (object[] values) =>
        {
            var typedValues = Array.ConvertAll(values, v => (TValue)v);
            await changedCallback(typedValues);
        });
    }

    /// <summary>
    /// This async method retrieves current values for the given settings, immediately invokes the callback with those values, and then registers the callback in an existing or newly created monitor stored in a dictionary, thereby subscribing it to future changes.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <param name="changedCallback">The changed callback.</param>
    /// <returns>A Task value produced by this method.</returns>

    public async Task WatchAsync(string[] settings, Func<object[], Task> changedCallback)
    {
        var values = FindValues(settings);

        await changedCallback(values);

        if (_monitors.TryGetValue(settings, out var monitor))
        {
            monitor.Callback += changedCallback;
        }
        else
        {
            monitor = new Monitor { LastValues = values, Callback = changedCallback };
            _monitors.Add(settings, monitor);
        }
    }

    /// <summary>
    /// Iterates through all monitors, compares each monitor&apos;s cached values to newly found values, and when they differ, updates the cached values and asynchronously invokes that monitor&apos;s callback with the new values.
    /// </summary>
    /// <returns>A Task value produced by this method.</returns>

    internal async Task NotifySettingsChangedAsync()
    {
        foreach (var item in _monitors)
        {
            var monitor = item.Value;
            var oldValues = monitor.LastValues;
            var newValues = FindValues(item.Key);
            if (!Enumerable.SequenceEqual(oldValues, newValues))
            {
                monitor.LastValues = newValues;
                await monitor.Callback(newValues);
            }
        }
    }

    /// <summary>
    /// Converts each string key in the input array to its corresponding value from the _settings collection, returning the results as an object array with no side effects.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>A object[] value produced by this method.</returns>

    private object[] FindValues(string[] settings) => Array.ConvertAll(settings, key => _settings[key]);

    /// <summary>
    /// This async void event handler awaits NotifySettingsChangedAsync, either directly or through _joinableTaskFactory.RunAsync when available, to notify settings changes without throwing exceptions.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The e.</param>

    private async void OnSettingsSaving(object sender, CancelEventArgs e)
    {
        if (_joinableTaskFactory != null)
        {
            await _joinableTaskFactory.RunAsync(NotifySettingsChangedAsync);
        }
        else
        {
            await NotifySettingsChangedAsync();
        }
    }

    private class Monitor
    {
        public Func<object[], Task> Callback;
        public object[] LastValues;
    }

    private class StringArrayComparer : IEqualityComparer<string[]>
    {
        private static readonly StringComparer ElementComparer = StringComparer.OrdinalIgnoreCase;

        /// <summary>
        /// Compares two string arrays element-by-element using ElementComparer and returns true if they are equal in order, otherwise false, with no side effects or exceptions.
        /// </summary>
        /// <param name="x">The x.</param>
        /// <param name="y">The y.</param>
        /// <returns>A bool value produced by this method.</returns>

        public bool Equals(string[] x, string[] y)
                    => Enumerable.SequenceEqual(x, y, ElementComparer);

        /// <summary>
        /// Computes a combined hash code for a string array by repeatedly XORing each element&apos;s hash with the running hash multiplied by 31 in an unchecked context, with no side effects.
        /// </summary>
        /// <param name="strings">The strings.</param>
        /// <returns>A int value produced by this method.</returns>

        public int GetHashCode(string[] strings)
        {
            int hash = 0;
            for (int i = 0; i < strings.Length; i++)
            {
                hash = unchecked(
                    hash * 31 ^ ElementComparer.GetHashCode(strings[i])
                );
            }

            return hash;
        }
    }
}
