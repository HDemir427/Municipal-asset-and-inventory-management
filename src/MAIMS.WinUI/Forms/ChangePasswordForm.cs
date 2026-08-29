using MAIMS.Data;
using MAIMS.WinUI.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Self-service password change form. The logged-in user enters their old
/// password (verified via BCrypt), then the new password twice. The new
/// password is BCrypt-hashed (cost 11) and saved.
/// </summary>
public class ChangePasswordForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TextBox _txtOldPassword;
    private readonly TextBox _txtNewPassword;
    private readonly TextBox _txtConfirmPassword;
    private readonly Button _btnSave;
    private readonly Button _btnCancel;
    private readonly Label _lblStatus;

    public ChangePasswordForm(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Text = "Change Password";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(500, 260);
        BackColor = MaimsTheme.Background;
        Font = MaimsTheme.Body;

        int y = 16;
        AddLabel("Current Password *:", y);
        _txtOldPassword = new TextBox
        {
            Location = new Point(180, y),
            Size = new Size(300, 24),
            Font = MaimsTheme.Body,
            PasswordChar = '*'
        };
        Controls.Add(_txtOldPassword);
        y += 32;

        AddLabel("New Password *:", y);
        _txtNewPassword = new TextBox
        {
            Location = new Point(180, y),
            Size = new Size(300, 24),
            Font = MaimsTheme.Body,
            PasswordChar = '*'
        };
        Controls.Add(_txtNewPassword);
        y += 32;

        AddLabel("Confirm New Password *:", y);
        _txtConfirmPassword = new TextBox
        {
            Location = new Point(180, y),
            Size = new Size(300, 24),
            Font = MaimsTheme.Body,
            PasswordChar = '*'
        };
        Controls.Add(_txtConfirmPassword);
        y += 45;

        // Buttons — use shorter labels so they fit comfortably.
        // "Change Password" → "Save" (primary), and Cancel.
        // Start from X=180 so they align with the textboxes above.
        _btnSave = MaimsTheme.CreateButton("Save", primary: true);
        _btnCancel = MaimsTheme.CreateButton("Cancel");
        MaimsTheme.LayoutButtons(180, y, 8, _btnSave, _btnCancel);
        _btnCancel.DialogResult = DialogResult.Cancel;
        _btnSave.Click += async (_, _) => await SaveAsync();
        Controls.Add(_btnSave);
        Controls.Add(_btnCancel);

        _lblStatus = new Label
        {
            Location = new Point(16, y + 45),
            Size = new Size(470, 30),
            Font = MaimsTheme.Small,
            ForeColor = MaimsTheme.TextSecondary
        };
        Controls.Add(_lblStatus);

        AcceptButton = _btnSave;
        CancelButton = _btnCancel;
    }

    private void AddLabel(string text, int y) =>
        Controls.Add(new Label
        {
            Text = text,
            Location = new Point(16, y + 3),
            Size = new Size(160, 20),
            Font = MaimsTheme.Body
        });

    private async Task SaveAsync()
    {
        // Validation
        if (string.IsNullOrWhiteSpace(_txtOldPassword.Text) ||
            string.IsNullOrWhiteSpace(_txtNewPassword.Text) ||
            string.IsNullOrWhiteSpace(_txtConfirmPassword.Text))
        {
            _lblStatus.Text = "All fields are required.";
            _lblStatus.ForeColor = MaimsTheme.Critical;
            return;
        }

        if (_txtNewPassword.Text != _txtConfirmPassword.Text)
        {
            _lblStatus.Text = "New password and confirmation do not match.";
            _lblStatus.ForeColor = MaimsTheme.Critical;
            return;
        }

        if (_txtNewPassword.Text.Length < 8)
        {
            _lblStatus.Text = "New password must be at least 8 characters.";
            _lblStatus.ForeColor = MaimsTheme.Critical;
            return;
        }

        if (_txtNewPassword.Text == _txtOldPassword.Text)
        {
            _lblStatus.Text = "New password must be different from the current password.";
            _lblStatus.ForeColor = MaimsTheme.Critical;
            return;
        }

        try
        {
            _btnSave.Enabled = false;
            _lblStatus.Text = "Verifying…";

            // Get current logged-in user's username from ICurrentSession
            string? username;
            using (var scope = _scopeFactory.CreateScope())
            {
                var session = scope.ServiceProvider.GetRequiredService<MAIMS.Core.Abstractions.ICurrentSession>();
                username = session.UserName;
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                _lblStatus.Text = "No active session. Please log in again.";
                _lblStatus.ForeColor = MaimsTheme.Critical;
                return;
            }

            // Verify old password + save new password
            using (var scope = _scopeFactory.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<MaimsDbContext>();
                var user = await ctx.Users.FirstOrDefaultAsync(u => u.Username == username);

                if (user is null)
                {
                    _lblStatus.Text = $"User '{username}' not found in database.";
                    _lblStatus.ForeColor = MaimsTheme.Critical;
                    return;
                }

                // Verify old password
                if (!BCrypt.Net.BCrypt.Verify(_txtOldPassword.Text, user.PasswordHash))
                {
                    _lblStatus.Text = "Current password is incorrect.";
                    _lblStatus.ForeColor = MaimsTheme.Critical;
                    return;
                }

                // Hash new password (cost 11 — matches AuthService.HashPassword)
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(_txtNewPassword.Text, workFactor: 11);
                await ctx.SaveChangesAsync();
            }

            _lblStatus.Text = "✓ Password changed successfully.";
            _lblStatus.ForeColor = MaimsTheme.OK;

            // Reset fields
            _txtOldPassword.Text = "";
            _txtNewPassword.Text = "";
            _txtConfirmPassword.Text = "";

            MessageBox.Show("Your password has been changed successfully.",
                "Password Changed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Error: " + ex.Message;
            _lblStatus.ForeColor = MaimsTheme.Critical;
        }
        finally
        {
            _btnSave.Enabled = true;
        }
    }
}
