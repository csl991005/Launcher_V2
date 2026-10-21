using System;
using System.Drawing;
using System.Windows.Forms;

namespace KartRider;

/// <summary>
/// 新增账号 / 重置口令对话框。纯代码构建界面，不依赖 Designer 文件。
///
/// 账号只用于登录校验，游戏昵称由玩家在「设置」里填写；
/// 这里的「游戏昵称」是可选绑定，填了之后该账号只能使用这个昵称。
/// </summary>
internal sealed class AccountEditForm : Form
{
    private readonly AuthAccount _existing;

    private readonly TextBox _accountBox;
    private readonly TextBox _passwordBox;
    private readonly TextBox _nicknameBox;
    private readonly TextBox _noteBox;
    private readonly CheckBox _randomBox;
    private readonly Button _okButton;
    private readonly Button _cancelButton;

    public string AccountName
    {
        get { return _accountBox.Text == null ? string.Empty : _accountBox.Text.Trim(); }
    }

    public string Password
    {
        get { return _passwordBox.Text == null ? string.Empty : _passwordBox.Text; }
    }

    public string Nickname
    {
        get { return _nicknameBox.Text == null ? string.Empty : _nicknameBox.Text.Trim(); }
    }

    public string Note
    {
        get { return _noteBox.Text == null ? string.Empty : _noteBox.Text.Trim(); }
    }

    /// <param name="existing">为 null 表示新增；否则为重置口令</param>
    public AccountEditForm(AuthAccount existing)
    {
        _existing = existing;

        _accountBox = new TextBox();
        _passwordBox = new TextBox();
        _nicknameBox = new TextBox();
        _noteBox = new TextBox();
        _randomBox = new CheckBox();
        _okButton = new Button();
        _cancelButton = new Button();

        InitializeComponent();

        if (existing != null)
        {
            Text = "重置口令";
            _accountBox.Text = existing.Account;
            _accountBox.ReadOnly = true;
            _accountBox.BackColor = SystemColors.Control;
            _nicknameBox.Text = existing.Nickname;
            _noteBox.Text = existing.Note;
        }
        else
        {
            Text = "新增账号";
        }
    }

    private void InitializeComponent()
    {
        Label accountLabel = new Label();
        Label passwordLabel = new Label();
        Label nicknameLabel = new Label();
        Label noteLabel = new Label();
        Label nicknameHint = new Label();
        SuspendLayout();

        accountLabel.AutoSize = true;
        accountLabel.Location = new Point(18, 22);
        accountLabel.Text = "账号";

        _accountBox.Location = new Point(86, 18);
        _accountBox.Size = new Size(276, 23);
        _accountBox.MaxLength = 32;

        passwordLabel.AutoSize = true;
        passwordLabel.Location = new Point(18, 56);
        passwordLabel.Text = "口令";

        _passwordBox.Location = new Point(86, 52);
        _passwordBox.Size = new Size(276, 23);
        _passwordBox.MaxLength = 128;

        _randomBox.AutoSize = true;
        _randomBox.Location = new Point(86, 82);
        _randomBox.Text = "随机生成口令（推荐）";
        _randomBox.Checked = _existing == null;
        _randomBox.CheckedChanged += RandomBox_CheckedChanged;

        nicknameLabel.AutoSize = true;
        nicknameLabel.Location = new Point(18, 112);
        nicknameLabel.Text = "游戏昵称";

        _nicknameBox.Location = new Point(86, 108);
        _nicknameBox.Size = new Size(276, 23);
        _nicknameBox.MaxLength = 32;

        nicknameHint.AutoSize = true;
        nicknameHint.ForeColor = Color.Gray;
        nicknameHint.Location = new Point(86, 134);
        nicknameHint.Text = "可留空，留空则由玩家在「设置」里填写";

        noteLabel.AutoSize = true;
        noteLabel.Location = new Point(18, 162);
        noteLabel.Text = "备注";

        _noteBox.Location = new Point(86, 158);
        _noteBox.Size = new Size(276, 23);
        _noteBox.MaxLength = 64;

        _okButton.Location = new Point(192, 200);
        _okButton.Size = new Size(80, 26);
        _okButton.Text = "确定";
        _okButton.UseVisualStyleBackColor = true;
        _okButton.Click += OkButton_Click;

        _cancelButton.Location = new Point(282, 200);
        _cancelButton.Size = new Size(80, 26);
        _cancelButton.Text = "取消";
        _cancelButton.UseVisualStyleBackColor = true;
        _cancelButton.DialogResult = DialogResult.Cancel;

        AutoScaleDimensions = new SizeF(6F, 12F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(384, 242);
        Font = new Font("宋体", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        Controls.Add(accountLabel);
        Controls.Add(_accountBox);
        Controls.Add(passwordLabel);
        Controls.Add(_passwordBox);
        Controls.Add(_randomBox);
        Controls.Add(nicknameLabel);
        Controls.Add(_nicknameBox);
        Controls.Add(nicknameHint);
        Controls.Add(noteLabel);
        Controls.Add(_noteBox);
        Controls.Add(_okButton);
        Controls.Add(_cancelButton);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        ResumeLayout(false);
        PerformLayout();

        RandomBox_CheckedChanged(null, EventArgs.Empty);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (_existing == null)
            _accountBox.Focus();
        else if (!_randomBox.Checked)
            _passwordBox.Focus();
    }

    private void RandomBox_CheckedChanged(object sender, EventArgs e)
    {
        _passwordBox.Enabled = !_randomBox.Checked;

        if (_randomBox.Checked)
            _passwordBox.Clear();
    }

    private void OkButton_Click(object sender, EventArgs e)
    {
        if (_existing == null)
        {
            if (!AuthStore.IsSafeAccountName(AccountName))
            {
                MessageBox.Show(this,
                    "账号不能为空、不超过 32 个字符，且不能包含 / \\ : * ? \" < > |",
                    "账号管理",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _accountBox.Focus();
                return;
            }
        }

        if (!string.IsNullOrEmpty(Nickname) && !AuthStore.IsSafeAccountName(Nickname))
        {
            MessageBox.Show(this,
                "游戏昵称不能超过 32 个字符，且不能包含 / \\ : * ? \" < > |",
                "账号管理",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _nicknameBox.Focus();
            return;
        }

        if (_randomBox.Checked)
        {
            _passwordBox.Text = AuthCrypto.GeneratePassword();
        }
        else if (Password.Length < 6)
        {
            MessageBox.Show(this, "口令至少 6 个字符。", "账号管理", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _passwordBox.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
