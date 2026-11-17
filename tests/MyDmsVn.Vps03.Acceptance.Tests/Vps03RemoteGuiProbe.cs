using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using MyDmsVn.Desktop.Infrastructure.Http;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03RemoteGuiProbe : IDisposable
{
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const string LoginTitle = "MyDmsVn — Đăng nhập";
    private readonly int _processId;

    internal Vps03RemoteGuiProbe(int processId)
    {
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        _processId = processId;
    }

    internal void WaitForLogin(TimeSpan timeout) => WaitForWindow(LoginTitle, timeout);

    internal void SubmitInvalidLogin(string username, string password, TimeSpan timeout)
    {
        var window = WaitForWindow(LoginTitle, timeout);
        SetValue(window, "Username", username);
        SetValue(window, "Password", password + "!");
        Invoke(window, "Đăng nhập");
        WaitForElement(window, "Tên đăng nhập hoặc mật khẩu không đúng.", timeout);
    }

    internal void SubmitValidLogin(string username, string password, TimeSpan timeout)
    {
        var window = WaitForWindow(LoginTitle, timeout);
        SetValue(window, "Username", username);
        SetValue(window, "Password", password);
        Invoke(window, "Đăng nhập");
        WaitForWindow("MyDmsVn", timeout);
    }

    internal Vps03RecoveryDialogObservation ObserveUnknownRecovery(
        HttpPendingWriteRecord expected, TimeSpan timeout)
    {
        var window = WaitForWindow("UNKNOWN write — operator recovery", timeout);
        var summary = Value(Find(window, "Exact pending write identity for support"));
        var check = Button(window, "Check server status");
        var accept = Button(window, "Accept certified NOT_EXECUTED");
        var warning = window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty,
                "UNKNOWN: Do not click Post, Save, or resend this command. The original write may already be committed. An HTTP error or missing status is never rollback evidence."));
        var forbidden = window.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
            .Cast<AutomationElement>()
            .Any(element => ForbiddenReleaseName(element.Current.Name));
        var shell = WaitForWindow("MyDmsVn", timeout);
        return new Vps03RecoveryDialogObservation(
            Has(summary, "Operation ID: " + expected.OperationId),
            Has(summary, "Actor user ID: " + expected.ActorUserId),
            Has(summary, "Deployment epoch: " + expected.DeploymentEpoch),
            Has(summary, "Operation kind: " + expected.Kind),
            Has(summary, "Method: " + expected.Method),
            Has(summary, "Route: " + expected.Route),
            Has(summary, "Target ID: (create)"),
            Has(summary, "Fingerprint SHA256: " + expected.Fingerprint),
            warning != null, !shell.Current.IsEnabled, check.Current.IsEnabled,
            !accept.Current.IsEnabled, !forbidden);
    }

    internal void WaitForUnknownRecoveryReleased(TimeSpan timeout)
    {
        var window = WaitForWindow("UNKNOWN write — operator recovery", timeout);
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            var summary = Value(Find(window, "Exact pending write identity for support"));
            var status = FindAutomationId(window, "RecoveryStatus").Current.Name;
            if (summary == "No pending write." &&
                (status.StartsWith("NOT_EXECUTED:", StringComparison.Ordinal) ||
                 status == "CLEAR: No pending write exists.") &&
                !Button(window, "Check server status").Current.IsEnabled &&
                !Button(window, "Accept certified NOT_EXECUTED").Current.IsEnabled)
                return;
            Thread.Sleep(100);
        }
        throw new TimeoutException("VPS03 Native UNKNOWN recovery did not release after confirmation.");
    }

    internal void CloseUnknownRecovery(TimeSpan timeout)
    {
        var window = WaitForWindow("UNKNOWN write — operator recovery", timeout);
        Invoke(window, "Close (keep fence)");
        WaitForWindowAbsent("UNKNOWN write — operator recovery", timeout);
    }

    public void Dispose() { }

    private AutomationElement WaitForWindow(string title, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        var condition = new PropertyCondition(AutomationElement.NameProperty, title);
        while (timer.Elapsed < timeout)
        {
            var candidates = AutomationElement.RootElement.FindAll(TreeScope.Descendants, condition);
            foreach (AutomationElement found in candidates)
            {
                GetWindowThreadProcessId(new IntPtr(found.Current.NativeWindowHandle), out var processId);
                if (processId == _processId) return found;
            }
            Thread.Sleep(100);
        }
        throw new TimeoutException("VPS03 attended window was not observed: " + title);
    }

    private void WaitForWindowAbsent(string title, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        var condition = new PropertyCondition(AutomationElement.NameProperty, title);
        while (timer.Elapsed < timeout)
        {
            var owned = AutomationElement.RootElement.FindAll(TreeScope.Descendants, condition)
                .Cast<AutomationElement>()
                .Any(found =>
                {
                    GetWindowThreadProcessId(new IntPtr(found.Current.NativeWindowHandle), out var processId);
                    return processId == _processId;
                });
            if (!owned) return;
            Thread.Sleep(100);
        }
        throw new TimeoutException("VPS03 attended window did not close: " + title);
    }

    private static void SetValue(AutomationElement window, string name, string value)
    {
        var element = FindInput(window, name);
        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            throw new InvalidOperationException("VPS03 attended input does not support ValuePattern: " + name);
        element.SetFocus();
        ((ValuePattern)pattern).SetValue(value);
    }

    private static AutomationElement FindInput(AutomationElement window, string name)
    {
        AutomationElement? editor = null;
        var candidates = window.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, name));
        foreach (AutomationElement candidate in candidates)
        {
            var type = candidate.Current.ControlType;
            if (type != ControlType.Edit && type != ControlType.ComboBox && type != ControlType.Spinner) continue;
            var inner = candidate.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)) ?? candidate;
            if (editor != null && !Automation.Compare(editor, inner))
                throw new InvalidOperationException("VPS03 attended input selector is ambiguous: " + name);
            editor = inner;
        }
        return editor ?? throw new InvalidOperationException("VPS03 attended input was not found: " + name);
    }

    private static void Invoke(AutomationElement window, string name)
    {
        var element = window.FindFirst(TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.NameProperty, name),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
            ?? throw new InvalidOperationException("VPS03 attended button was not found: " + name);
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
        {
            ((InvokePattern)invoke).Invoke();
            return;
        }
        Click(window, element);
    }

    private static void Click(AutomationElement window, AutomationElement element)
    {
        var point = element.GetClickablePoint();
        GetCursorPos(out var original);
        SetForegroundWindow(new IntPtr(window.Current.NativeWindowHandle));
        SetCursorPos((int)point.X, (int)point.Y);
        mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        SetCursorPos(original.X, original.Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        internal int X;
        internal int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr windowHandle, StringBuilder value, int maximum);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr windowHandle);

    private static AutomationElement Find(AutomationElement window, string name) =>
        window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, name))
        ?? throw new InvalidOperationException("VPS03 attended element was not found: " + name);

    private static AutomationElement Button(AutomationElement window, string name) =>
        window.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.NameProperty, name),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
        ?? throw new InvalidOperationException("VPS03 attended button was not found: " + name);

    private static AutomationElement FindAutomationId(AutomationElement window, string id) =>
        window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, id))
        ?? throw new InvalidOperationException("VPS03 attended automation id was not found: " + id);

    private static string Value(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value))
            return ((ValuePattern)value).Current.Value;
        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var text))
            return ((TextPattern)text).DocumentRange.GetText(-1).TrimEnd('\r', '\n');
        var handle = new IntPtr(element.Current.NativeWindowHandle);
        var length = GetWindowTextLength(handle);
        if (length > 0 && length <= 4096)
        {
            var buffer = new StringBuilder(length + 1);
            if (GetWindowText(handle, buffer, buffer.Capacity) > 0) return buffer.ToString();
        }
        throw new InvalidOperationException("VPS03 attended text value was unavailable.");
    }

    private static bool ForbiddenReleaseName(string value) =>
        value.IndexOf("force", StringComparison.OrdinalIgnoreCase) >= 0 ||
        value.IndexOf("retry", StringComparison.OrdinalIgnoreCase) >= 0 ||
        value.IndexOf("resend", StringComparison.OrdinalIgnoreCase) >= 0 ||
        value.IndexOf("clear", StringComparison.OrdinalIgnoreCase) >= 0 ||
        value.IndexOf("release", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool Has(string value, string expected) =>
        value.IndexOf(expected, StringComparison.Ordinal) >= 0;

    private static void WaitForElement(AutomationElement window, string name, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            var candidates = window.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Pane));
            foreach (AutomationElement candidate in candidates)
                if (candidate.Current.Name.StartsWith(name, StringComparison.Ordinal)) return;
            Thread.Sleep(100);
        }
        throw new TimeoutException("VPS03 attended element was not observed: " + name);
    }
}

internal sealed class Vps03RecoveryDialogObservation
{
    internal Vps03RecoveryDialogObservation(bool operation, bool actor, bool epoch,
        bool kind, bool method, bool route, bool target, bool fingerprint, bool warning,
        bool shellFenced, bool checkEnabled, bool confirmationDisabled, bool noForceRelease)
    {
        Operation = operation; Actor = actor; Epoch = epoch; Kind = kind; Method = method;
        Route = route; Target = target; Fingerprint = fingerprint; Warning = warning;
        ShellFenced = shellFenced; CheckEnabled = checkEnabled;
        ConfirmationDisabled = confirmationDisabled; NoForceRelease = noForceRelease;
    }
    internal bool Operation { get; }
    internal bool Actor { get; }
    internal bool Epoch { get; }
    internal bool Kind { get; }
    internal bool Method { get; }
    internal bool Route { get; }
    internal bool Target { get; }
    internal bool Fingerprint { get; }
    internal bool Warning { get; }
    internal bool ShellFenced { get; }
    internal bool CheckEnabled { get; }
    internal bool ConfirmationDisabled { get; }
    internal bool NoForceRelease { get; }
    internal bool Complete => Operation && Actor && Epoch && Kind && Method && Route && Target &&
        Fingerprint && Warning && ShellFenced && CheckEnabled && ConfirmationDisabled && NoForceRelease;
}
