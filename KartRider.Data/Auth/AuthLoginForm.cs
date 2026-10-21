using System;
using System.Drawing;
using System.Windows.Forms;

namespace KartRider;

/// <summary>
/// 账号登录窗口。纯代码构建界面，不依赖 Designer 文件，避免与上游的窗体改动冲突。
/// </summary>
internal sealed class AuthLoginForm : Form
{
    private readonly TextBox _accountBox;
    private readonly TextBox _passwordBox;
    private readonly CheckBox _rememberBox;
    private readonly CheckBox _skipBox;
    private readonly Button _okButton;
    private readonly Button _cancelButton;

    /// <summary>用户选择跳过认证（服务器未启用认证时使用）。</summary>
    public bool SkipAuth { get; private set; }

    public string Account
    {
        get { return _accountBox.Text == null ? string.Empty : _accountBox.Text.Trim(); }
    }

    public string Password
    {
        get { return _passwordBox.Text == null ? string.Empty : _passwordBox.Text; }
    }

    public bool RememberAccount
    {
        get { return _rememberBox.Checked; }
    }

    public AuthLoginForm(string lastAccount)
    {
        _accountBox = new TextBox();
        _passwordBox = new TextBox();
        _rememberBox = new CheckBox();
        _skipBox = new CheckBox();
        _okButton = new Button();
        _cancelButton = new Button();

        InitializeComponent();

        if (!string.IsNullOrEmpty(lastAccount))
            _accountBox.Text = lastAccount;
    }

    private void InitializeComponent()
    {
        Label accountLabel = new Label();
        Label passwordLabel = new Label();
        Label hintLabel = new Label();
        SuspendLayout();

        // 账号
        accountLabel.AutoSize = true;
        accountLabel.Location = new Point(20, 22);
        accountLabel.Text = "账号";

        _accountBox.Location = new Point(76, 18);
        _accountBox.Size = new Size(244, 23);
        _accountBox.MaxLength = 32;

        // 密码
        passwordLabel.AutoSize = true;
        passwordLabel.Location = new Point(20, 54);
        passwordLabel.Text = "密码";

        _passwordBox.Location = new Point(76, 50);
        _passwordBox.Size = new Size(244, 23);
        _passwordBox.MaxLength = 128;
        _passwordBox.UseSystemPasswordChar = true;

        // 记住账号
        _rememberBox.AutoSize = true;
        _rememberBox.Location = new Point(76, 80);
        _rememberBox.Text = "记住账号（不保存密码）";
        _rememberBox.Checked = true;

        // 跳过认证
        _skipBox.AutoSize = true;
        _skipBox.Location = new Point(76, 104);
        _skipBox.Text = "服务器未启用账号认证，直接启动";
        _skipBox.CheckedChanged += SkipBox_CheckedChanged;

        // 提示
        hintLabel.AutoSize = true;
        hintLabel.ForeColor = Color.Gray;
        hintLabel.Location = new Point(20, 130);
        hintLabel.Text = "账号与密码由服务器管理员分配，账号即游戏昵称。";

        // 按钮
        _okButton.Location = new Point(156, 160);
        _okButton.Size = new Size(80, 26);
        _okButton.Text = "登录并启动";
        _okButton.UseVisualStyleBackColor = true;
        _okButton.Click += OkButton_Click;

        _cancelButton.Location = new Point(242, 160);
        _cancelButton.Size = new Size(80, 26);
        _cancelButton.Text = "取消";
        _cancelButton.UseVisualStyleBackColor = true;
        _cancelButton.DialogResult = DialogResult.Cancel;

        // 窗体
        AutoScaleDimensions = new SizeF(6F, 12F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(340, 202);
        Font = new Font("宋体", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "账号登录";

        Controls.Add(accountLabel);
        Controls.Add(_accountBox);
        Controls.Add(passwordLabel);
        Controls.Add(_passwordBox);
        Controls.Add(_rememberBox);
        Controls.Add(_skipBox);
        Controls.Add(hintLabel);
        Controls.Add(_okButton);
        Controls.Add(_cancelButton);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (_accountBox.TextLength == 0)
            _accountBox.Focus();
        else
            _passwordBox.Focus();
    }

    private void SkipBox_CheckedChanged(object sender, EventArgs e)
    {
        bool skip = _skipBox.Checked;
        _passwordBox.Enabled = !skip;

        if (skip)
            _passwordBox.Clear();
    }

    private void OkButton_Click(object sender, EventArgs e)
    {
        if (_skipBox.Checked)
        {
            SkipAuth = true;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        if (Account.Length == 0)
        {
            MessageBox.Show(this, "请输入账号。", "账号登录", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _accountBox.Focus();
            return;
        }

        if (Password.Length == 0)
        {
            MessageBox.Show(this, "请输入密码。", "账号登录", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _passwordBox.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
