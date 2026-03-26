using System;
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
        /// </summary>
        public static async Task<UiaResult> WaitAndInvokeAsync(
            string[] names, string description,
            TimeSpan timeout, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                var result = InvokeButton(names, description);
                if (result.Success)
                    return result;

                await Task.Delay(1500, ct);
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
            var egsWindow = FindEgsWindow();
            if (egsWindow == null)
                return "(EGS window not found)";

            var sb = new System.Text.StringBuilder();
            DumpElement(sb, egsWindow, 0, maxDepth);
            return sb.ToString();
        }

        // ---------------------------------------------------------------
        //  Private implementation
        // ---------------------------------------------------------------

        private static UiaResult InvokeButton(string[] candidateNames, string description)
        {
            var egsWindow = FindEgsWindow();
            if (egsWindow == null)
                return UiaResult.Fail("EGS window not found in the automation tree.");

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
            var allButtons = egsWindow.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty,
                    ControlType.Button));

            if (allButtons != null)
            {
                foreach (AutomationElement btn in allButtons)
                {
                    string btnName = btn.Current.Name;
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
                    var sectionButtons = section.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty,
                            ControlType.Button));

                    if (sectionButtons != null && sectionButtons.Count > 0)
                    {
                        // Try the first button — in the downloads section the
                        // primary action button is typically pause or resume.
                        var first = sectionButtons[0];
                        var invokeResult = TryInvoke(first,
                            first.Current.Name ?? "(unnamed button in " + hint + ")");
                        if (invokeResult.Success)
                            return invokeResult;
                    }
                }
            }

            return UiaResult.Fail(
                string.Format("Could not find a {0} button in the EGS window. "
                    + "The UI may have changed or the download view may not be visible.",
                    description));
        }

        /// <summary>
        /// Find the main EGS window. CEF apps typically have a single
        /// top-level window for the process.
        /// </summary>
        private static AutomationElement FindEgsWindow()
        {
            var procs = Process.GetProcessesByName("EpicGamesLauncher");
            if (procs.Length == 0)
                return null;

            // Try each process — EGS may have multiple processes (helpers),
            // but only the main one has a visible window.
            foreach (var proc in procs)
            {
                try
                {
                    if (proc.MainWindowHandle == IntPtr.Zero)
                        continue;

                    var element = AutomationElement.FromHandle(proc.MainWindowHandle);
                    if (element != null)
                        return element;
                }
                catch
                {
                    // Process exited or access denied — skip
                }
            }

            // Fallback: search the desktop for a window whose process
            // matches one of the EGS PIDs.
            int[] pids = procs.Select(p => p.Id).ToArray();
            try
            {
                var desktop = AutomationElement.RootElement;
                var windows = desktop.FindAll(TreeScope.Children,
                    Condition.TrueCondition);

                foreach (AutomationElement win in windows)
                {
                    try
                    {
                        if (pids.Contains(win.Current.ProcessId))
                            return win;
                    }
                    catch { /* stale element */ }
                }
            }
            catch { /* desktop enumeration failed */ }

            return null;
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
