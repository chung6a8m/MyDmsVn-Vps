using System;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using MyDmsVn.Desktop.Application;

namespace MyDmsVn.Desktop.WinForms
{
    /// <summary>Attended support surface. No replay, reset, force-close or body disclosure.</summary>
    public sealed class DesktopWriteRecoveryDialog : Form
    {
        private readonly IDesktopWriteRecovery _recovery;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly TextBox _summary;
        private readonly Label _status;
        private readonly Button _check;
        private readonly Button _accept;

        public DesktopWriteRecoveryDialog(IDesktopWriteRecovery recovery)
        {
            _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
            Text = "UNKNOWN write — operator recovery";
            Name = "DesktopWriteRecoveryDialog";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(580, 390);
            Size = new Size(720, 470);
            AutoScaleMode = AutoScaleMode.Dpi;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(16),
                ColumnCount = 1, RowCount = 4
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var warning = new Label
            {
                AutoSize = true, MaximumSize = new Size(630, 0),
                ForeColor = Color.DarkRed,
                Text = "UNKNOWN: Do not click Post, Save, or resend this command. " +
                       "The original write may already be committed. " +
                       "An HTTP error or missing status is never rollback evidence."
            };
            _summary = new TextBox
            {
                Name = "PendingWriteIdentity",
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill, WordWrap = false,
                AccessibleName = "Exact pending write identity for support"
            };
            _status = new Label
            {
                Name = "RecoveryStatus", AutoSize = true,
                MaximumSize = new Size(630, 0),
                Text = "Awaiting an authenticated status check. The write fence remains active."
            };
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill, AutoSize = true, WrapContents = true
            };
            var close = new Button { Text = "Close (keep fence)", AutoSize = true, DialogResult = DialogResult.Cancel };
            _check = new Button { Text = "Check server status", AutoSize = true };
            _accept = new Button { Text = "Accept certified NOT_EXECUTED", AutoSize = true, Enabled = false };
            buttons.Controls.Add(close);
            buttons.Controls.Add(_accept);
            buttons.Controls.Add(_check);
            layout.Controls.Add(warning, 0, 0);
            layout.Controls.Add(_summary, 0, 1);
            layout.Controls.Add(_status, 0, 2);
            layout.Controls.Add(buttons, 0, 3);
            Controls.Add(layout);
            CancelButton = close;
            _check.Click += async (_, _) => await CheckAsync();
            _accept.Click += async (_, _) => await AcceptAsync();
            RefreshIdentity();
        }

        private void RefreshIdentity()
        {
            var pending = _recovery.Pending;
            if (pending == null)
            {
                _summary.Text = "No pending write.";
                _check.Enabled = false;
                _accept.Enabled = false;
                return;
            }
            _summary.Text =
                "Status: UNKNOWN (durable fence)" + Environment.NewLine +
                "Operation ID: " + pending.OperationId + Environment.NewLine +
                "Actor user ID: " + pending.ActorUserId.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
                "Protocol: " + pending.Protocol + Environment.NewLine +
                "Operation kind: " + pending.Kind + Environment.NewLine +
                "Method: " + pending.Method + Environment.NewLine +
                "Route: " + pending.Route + Environment.NewLine +
                "Target ID: " + (pending.TargetId?.ToString(CultureInfo.InvariantCulture) ?? "(create)") + Environment.NewLine +
                "Deployment epoch: " + (pending.DeploymentEpoch ?? "(no journal)") + Environment.NewLine +
                "Fingerprint SHA256: " + pending.Fingerprint + Environment.NewLine +
                "Recorded at UTC: " + pending.CreatedUtc.ToString("O", CultureInfo.InvariantCulture);
        }

        private async System.Threading.Tasks.Task CheckAsync()
        {
            _check.Enabled = false;
            _accept.Enabled = false;
            try
            {
                var result = await _recovery.CheckAsync(_lifetime.Token);
                if (IsDisposed) return;
                _status.Text = result.State + ": " + result.Explanation +
                    (result.CaseId == null ? "" : Environment.NewLine + "Operator case: " + result.CaseId);
                _accept.Enabled = result.State == "NOT_EXECUTED" && !result.FenceReleased &&
                    !string.IsNullOrEmpty(result.CaseId);
                RefreshIdentity();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception)
            {
                if (!IsDisposed)
                    _status.Text = "UNKNOWN: Status read failed. The durable fence remains active.";
            }
            finally { if (!IsDisposed) _check.Enabled = _recovery.Pending != null; }
        }

        private async System.Threading.Tasks.Task AcceptAsync()
        {
            if (MessageBox.Show(this,
                "Accept ONLY the current exact server-audited NOT_EXECUTED certificate? " +
                "The original command will never be resent. Refresh the authoritative baseline before a NEW operation.",
                "Confirm terminal certificate", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            _check.Enabled = false;
            _accept.Enabled = false;
            try
            {
                // The service independently rereads and validates the certificate at click time.
                var result = await _recovery.AcceptCertifiedNotExecutedAsync(_lifetime.Token);
                if (IsDisposed) return;
                _status.Text = result.State + ": " + result.Explanation;
                RefreshIdentity();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception)
            {
                if (!IsDisposed)
                    _status.Text = "UNKNOWN: Certificate verification failed. The write remains fenced.";
            }
            finally { if (!IsDisposed) _check.Enabled = _recovery.Pending != null; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _lifetime.Cancel();
                _lifetime.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
