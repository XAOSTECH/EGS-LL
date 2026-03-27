using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace EgsLL.Core
{
    /// <summary>
    /// Uses the Windows UI Automation (UIA) API to interact with the
    /// Epic Games Store launcher. CEF/Chromium-based apps expose their
    /// UI through the Windows accessibility tree, allowing us to find
    /// and invoke controls (pause, resume, install) programmatically
    /// without elevation or process manipulation.
    ///
    /// EGS may use a separate download-manager window/process distinct
    /// from the main launcher. We search all known process names and
    /// also scan the desktop for windows with EGS-related titles.
    ///
    /// Strategy cascade for finding controls:
    ///   1. Name property (localised button text)
    ///   2. AutomationId (stable across locales if set)
    ///   3. ControlType + tree position (fallback)
    ///
    /// All public methods return a result struct so the caller knows
    /// exactly what happened and can decide on the next fallback.
    /// </summary>
    public static class UIAutomationHelper
    {
        // Known button labels (English). EGS is CEF; these come from
        // the Chromium accessibility name, which mirrors the visible text.
        // Future: add localised variants or use AutomationId if stable.
        private static readonly string[] PauseNames =
            { "Pause", "PAUSE", "pause", "Pause All", "PAUSE ALL" };

        private static readonly string[] ResumeNames =
            { "Resume", "RESUME", "resume", "Resume All", "RESUME ALL" };

        // Substring patterns for the download / install section
        private static readonly string[] DownloadSectionHints =
            { "Downloads", "DOWNLOADS", "download" };

        // Process names to search — EGS may split downloads into a
        // separate process from the main launcher.
        private static readonly string[] EgsProcessNames =
        {
            "EpicGamesLauncher",
            "EpicGamesDownloadManager",
            "EpicInstaller",
            "EpicWebHelper"
        };

        // Window title substrings for fallback desktop scan
        private static readonly string[] EgsWindowTitleHints =
        {
            "Epic Games",
            "Download Manager",
            "Downloads"
        };

        // Hard ceiling for a single InvokeButton attempt. Prevents
        // FindAll(TreeScope.Descendants) on CEF trees from blocking
        // the async timeout indefinitely.
        private static readonly TimeSpan PerAttemptTimeout =
            TimeSpan.FromSeconds(5);

        /// <summary>Result of a UIA operation.</summary>
        public struct UiaResult
        {
            public bool Success;
            public string Detail;

            public static UiaResult Ok(string detail = null)
            {
                return new UiaResult { Success = true, Detail = detail };
            }

            public static UiaResult Fail(string detail)
            {
                return new UiaResult { Success = false, Detail = detail };
            }
        }

        /// <summary>
        /// Attempt to click the Pause button in the EGS downloads view.
        /// </summary>
        public static UiaResult PauseDownload()
        {
            return InvokeButton(PauseNames, "pause");
        }

        /// <summary>
        /// Attempt to click the Resume button in the EGS downloads view.
        /// </summary>
        public static UiaResult ResumeDownload()
        {
            return InvokeButton(ResumeNames, "resume");
        }

        /// <summary>
        /// Wait for a button matching one of the given names to appear,
        /// then invoke it. Useful when launching an install — the pause
        /// button only appears once the download actually starts.
        ///
        /// Each attempt runs InvokeButton on a thread-pool thread with
        /// a hard per-attempt ceiling so that slow UIA tree walks
        /// (common with CEF/Chromium apps) cannot block indefinitely.
        /// </summary>
        public static async Task<UiaResult> WaitAndInvokeAsync(
            string[] names, string description,
            TimeSpan timeout, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                // Run InvokeButton off the UI thread with a hard timeout.
                // If FindAll(TreeScope.Descendants) stalls on a CEF tree,
                // the per-attempt timeout lets us move on.
                var attemptTask = Task.Run(() => InvokeButton(names, description), ct);
                var delayTask = Task.Delay(PerAttemptTimeout, ct);

                var winner = await Task.WhenAny(attemptTask, delayTask);

                if (winner == attemptTask)
                {
                    var result = attemptTask.Result;
                    if (result.Success)
                        return result;
                }
                // else: attempt timed out or button not found yet — retry

                // Small gap before next attempt to avoid tight-looping
                if (DateTime.UtcNow < deadline)
                    await Task.Delay(1000, ct);
            }

            return ct.IsCancellationRequested
                ? UiaResult.Fail("Cancelled while waiting for " + description + " button.")
                : UiaResult.Fail("Timed out waiting for " + description + " button to appear.");
        }

        /// <summary>
        /// Wait for the Pause button to appear then click it.
        /// </summary>
        public static Task<UiaResult> WaitAndPauseAsync(
            TimeSpan timeout, CancellationToken ct)
        {
            return WaitAndInvokeAsync(PauseNames, "pause", timeout, ct);
        }

        /// <summary>
        /// Dump the immediate subtree of the EGS window for diagnostic
        /// purposes. Returns a multi-line string describing the first
        /// few levels of the UIA tree. Useful for discovering control
        /// names and types when adapting to EGS UI changes.
        /// </summary>
        public static string DumpTree(int maxDepth = 3)
        {
            var windows = FindEgsWindows();
            if (windows.Length == 0)
                return "(No EGS windows found)";

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < windows.Length; i++)
            {
                sb.AppendFormat("=== Window {0} ===\n", i + 1);
                DumpElement(sb, windows[i], 0, maxDepth);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------
        //  Private implementation
        // ---------------------------------------------------------------

        private static UiaResult InvokeButton(string[] candidateNames, string description)
        {
            var windows = FindEgsWindows();
            if (windows.Length == 0)
                return UiaResult.Fail("No EGS windows found in the automation tree.");

            foreach (var egsWindow in windows)
            {
                // Strategy 1: search by Name property
                foreach (string name in candidateNames)
                {
                    var button = FindDescendant(egsWindow,
                        new PropertyCondition(AutomationElement.NameProperty, name,
                            PropertyConditionFlags.IgnoreCase));

                    if (button != null)
                    {
                        var invokeResult = TryInvoke(button, name);
                        if (invokeResult.Success)
                            return invokeResult;
                    }
                }

                // Strategy 2: search by ControlType.Button, then match Name substring
                var allButtons = FindAllSafe(egsWindow,
                    new PropertyCondition(AutomationElement.ControlTypeProperty,
                        ControlType.Button));

                if (allButtons != null)
                {
                    foreach (AutomationElement btn in allButtons)
                    {
                        string btnName;
                        try { btnName = btn.Current.Name; }
                        catch { continue; }

                        if (string.IsNullOrEmpty(btnName))
                            continue;

                        foreach (string candidate in candidateNames)
                        {
                            if (btnName.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var invokeResult = TryInvoke(btn, btnName);
                                if (invokeResult.Success)
                                    return invokeResult;
                            }
                        }
                    }
                }

                // Strategy 3: search within download-section subtree
                foreach (string hint in DownloadSectionHints)
                {
                    var section = FindDescendant(egsWindow,
                        new PropertyCondition(AutomationElement.NameProperty, hint,
                            PropertyConditionFlags.IgnoreCase));

                    if (section != null)
                    {
                        var sectionButtons = FindAllSafe(section,
                            new PropertyCondition(AutomationElement.ControlTypeProperty,
                                ControlType.Button));

                        if (sectionButtons != null && sectionButtons.Count > 0)
                        {
                            var first = sectionButtons[0];
                            string firstName;
                            try { firstName = first.Current.Name ?? "(unnamed)"; }
                            catch { firstName = "(stale)"; }

                            var invokeResult = TryInvoke(first, firstName);
                            if (invokeResult.Success)
                                return invokeResult;
                        }
                    }
                }
            }

            return UiaResult.Fail(
                string.Format("Could not find a {0} button across {1} EGS window(s). "
                    + "The UI may have changed or the download view may not be visible.",
                    description, windows.Length));
        }

        /// <summary>
        /// Find all EGS-related windows. Searches known process names
        /// first (main launcher, download manager, installer), then
        /// falls back to scanning desktop windows by title.
        /// Returns multiple windows because the download controls may
        /// live in a separate window from the main launcher.
        /// </summary>
        private static AutomationElement[] FindEgsWindows()
        {
            var results = new List<AutomationElement>();
            var seenPids = new HashSet<int>();

            // Search all known EGS process names
            foreach (var procName in EgsProcessNames)
            {
                Process[] procs;
                try { procs = Process.GetProcessesByName(procName); }
                catch { continue; }

                foreach (var proc in procs)
                {
                    try
                    {
                        if (proc.MainWindowHandle == IntPtr.Zero)
                            continue;
                        if (!seenPids.Add(proc.Id))
                            continue;

                        var element = AutomationElement.FromHandle(proc.MainWindowHandle);
                        if (element != null)
                            results.Add(element);
                    }
                    catch { /* process exited or access denied */ }
                }
            }

            // Fallback: scan the desktop for windows with EGS-related titles
            // (catches renamed/unknown download manager processes)
            try
            {
                var desktop = AutomationElement.RootElement;
                var windows = desktop.FindAll(TreeScope.Children,
                    Condition.TrueCondition);

                foreach (AutomationElement win in windows)
                {
                    try
                    {
                        int pid = win.Current.ProcessId;
                        if (seenPids.Contains(pid))
                            continue;

                        string name = win.Current.Name;
                        if (string.IsNullOrEmpty(name))
                            continue;

                        foreach (var hint in EgsWindowTitleHints)
                        {
                            if (name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                seenPids.Add(pid);
                                results.Add(win);
                                break;
                            }
                        }
                    }
                    catch { /* stale element */ }
                }
            }
            catch { /* desktop enumeration failed */ }

            return results.ToArray();
        }

        /// <summary>
        /// Safe wrapper around FindAll that swallows exceptions from
        /// stale elements or unresponsive UIA providers.
        /// </summary>
        private static AutomationElementCollection FindAllSafe(
            AutomationElement root, Condition condition)
        {
            try
            {
                return root.FindAll(TreeScope.Descendants, condition);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Try to invoke a control using the InvokePattern.
        /// Falls back to Toggle or SelectionItem if Invoke is not supported.
        /// </summary>
        private static UiaResult TryInvoke(AutomationElement element, string name)
        {
            try
            {
                // Prefer InvokePattern (standard button click)
                object pattern;
                if (element.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
                {
                    ((InvokePattern)pattern).Invoke();
                    return UiaResult.Ok("Invoked '" + name + "' via InvokePattern.");
                }

                // Some CEF buttons expose TogglePattern instead
                if (element.TryGetCurrentPattern(TogglePattern.Pattern, out pattern))
                {
                    ((TogglePattern)pattern).Toggle();
                    return UiaResult.Ok("Toggled '" + name + "' via TogglePattern.");
                }

                return UiaResult.Fail(
                    "Found '" + name + "' but it has no invocable pattern.");
            }
            catch (ElementNotAvailableException)
            {
                return UiaResult.Fail("'" + name + "' disappeared before it could be invoked.");
            }
            catch (Exception ex)
            {
                return UiaResult.Fail("Error invoking '" + name + "': " + ex.Message);
            }
        }

        /// <summary>
        /// Find the first descendant matching a condition.
        /// </summary>
        private static AutomationElement FindDescendant(
            AutomationElement root, Condition condition)
        {
            try
            {
                return root.FindFirst(TreeScope.Descendants, condition);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Recursive tree dump for diagnostics.
        /// </summary>
        private static void DumpElement(
            System.Text.StringBuilder sb,
            AutomationElement el, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;

            try
            {
                string indent = new string(' ', depth * 2);
                sb.AppendFormat("{0}[{1}] Name=\"{2}\" AutomationId=\"{3}\"\n",
                    indent,
                    el.Current.ControlType.ProgrammaticName,
                    el.Current.Name ?? "",
                    el.Current.AutomationId ?? "");

                var children = el.FindAll(TreeScope.Children, Condition.TrueCondition);
                if (children != null)
                {
                    // Cap output — the tree can be enormous in CEF apps
                    int count = 0;
                    foreach (AutomationElement child in children)
                    {
                        if (count++ > 50)
                        {
                            sb.AppendFormat("{0}  ... ({1} more children)\n",
                                indent, children.Count - 50);
                            break;
                        }
                        DumpElement(sb, child, depth + 1, maxDepth);
                    }
                }
            }
            catch { /* stale element */ }
        }
    }
}
