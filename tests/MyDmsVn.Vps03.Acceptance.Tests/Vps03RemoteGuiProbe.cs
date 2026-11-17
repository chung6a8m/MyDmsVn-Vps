using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03RemoteGuiProbe : IDisposable
{
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
        var element = Find(window, name);
        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            throw new InvalidOperationException("VPS03 attended input does not support ValuePattern: " + name);
        ((ValuePattern)pattern).SetValue(value);
    }

    private static void Invoke(AutomationElement window, string name)
    {
        var element = Find(window, name);
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
        {
            ((InvokePattern)invoke).Invoke();
            return;
        }
        element.SetFocus();
        SendKeys.SendWait("{ENTER}");
    }

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
