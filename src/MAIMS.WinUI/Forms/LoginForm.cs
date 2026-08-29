using MAIMS.Core.Interfaces;
using MAIMS.WinUI.Theming;

namespace MAIMS.WinUI.Forms;

/// <summary>
/// Login dialog. Authenticates against the database via IAuthService.
/// Returns DialogResult.OK on success; anything else aborts the application.
/// </summary>
public class LoginForm : Form
{
    private readonly IAuthService _auth;
    private readonly TextBox _txtUsername;
    private readonly TextBox _txtPassword;
    private readonly Button _btnLogin;
    private readonly Button _btnExit;
    private readonly Label _lblError;

    public LoginForm(IAuthService auth)
    {
        _auth = auth;
        Text = "MAIMS — Login";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 260);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = MaimsTheme.Background;
        Font = MaimsTheme.Body;

        var lblTitle = new Label
        {
            Text = "Municipal Asset & Inventory Management",
            Font = MaimsTheme.Heading,
            ForeColor = MaimsTheme.Primary,
            Location = new Point(20, 15),
            Size = new Size(380, 30),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var lblSub = new Label
        {
            Text = "Please sign in to continue.",
            Font = MaimsTheme.Small,
            ForeColor = MaimsTheme.TextSecondary,
            Location = new Point(20, 45),
            Size = new Size(380, 18)
        };

        var lblUser = new Label { Text = "Username", Location = new Point(20, 80), Size = new Size(100, 20), Font = MaimsTheme.Body };
        _txtUsername = new TextBox { Location = new Point(130, 78), Size = new Size(260, 24), Font = MaimsTheme.Body };

        var lblPass = new Label { Text = "Password", Location = new Point(20, 110), Size = new Size(100, 20), Font = MaimsTheme.Body };
        _txtPassword = new TextBox { Location = new Point(130, 108), Size = new Size(260, 24), Font = MaimsTheme.Body, PasswordChar = '*' };

        // Buttons via MaimsTheme.CreateButton — guarantees text is fully visible
        // on all DPIs by using AutoSize + GrowAndShrink + TextAlign.MiddleCenter.
        _btnLogin = MaimsTheme.CreateButton("Sign in", primary: true);
        _btnExit = MaimsTheme.CreateButton("Exit");
        MaimsTheme.LayoutButtons(130, 145, 8, _btnLogin, _btnExit);
        _btnLogin.Click += OnLoginClick;
        _btnExit.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        _lblError = new Label
        {
            Text = "",
            ForeColor = MaimsTheme.Critical,
            Location = new Point(20, 195),
            Size = new Size(380, 50),
            Font = MaimsTheme.Small
        };

        Controls.AddRange(new Control[] { lblTitle, lblSub, lblUser, _txtUsername, lblPass, _txtPassword, _btnLogin, _btnExit, _lblError });

        AcceptButton = _btnLogin;
        CancelButton = _btnExit;
        _txtUsername.Focus();
    }

    private async void OnLoginClick(object? sender, EventArgs e)
    {
        _btnLogin.Enabled = false;
        _lblError.Text = "";
        try
        {
            var result = await _auth.LoginAsync(_txtUsername.Text.Trim(), _txtPassword.Text);
            if (result.Success)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                _lblError.Text = result.ErrorMessage ?? "Login failed.";
            }
        }
        catch (Exception ex)
        {
            _lblError.Text = "Error: " + ex.Message;
        }
        finally
        {
            _btnLogin.Enabled = true;
        }
    }
}
