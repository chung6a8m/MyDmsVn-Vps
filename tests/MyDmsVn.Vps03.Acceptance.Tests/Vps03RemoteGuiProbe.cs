using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

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

    public void Dispose() { }

    private AutomationElement WaitForWindow(string title, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        var condition = new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, _processId),
            new PropertyCondition(AutomationElement.NameProperty, title));
        while (timer.Elapsed < timeout)
        {
            var found = AutomationElement.RootElement.FindFirst(TreeScope.Children, condition);
            if (found != null) return found;
            Thread.Sleep(100);
        }
        throw new TimeoutException("VPS03 attended window was not observed: " + title);
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

    private static AutomationElement Find(AutomationElement window, string name) =>
        window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, name))
        ?? throw new InvalidOperationException("VPS03 attended element was not found: " + name);

    private static void WaitForElement(AutomationElement window, string name, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            var found = window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, name));
            if (found != null) return;
            Thread.Sleep(100);
        }
        throw new TimeoutException("VPS03 attended element was not observed: " + name);
    }
}
